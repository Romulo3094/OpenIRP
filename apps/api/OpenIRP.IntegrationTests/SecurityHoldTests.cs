using System.Net;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenIRP.Application.Incidents;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using Npgsql;
using Xunit;
namespace OpenIRP.IntegrationTests;
public sealed partial class IdentityAdministrationTests
{
    private Task<SecurityHoldItem> HoldVersion(ExecutionFixture f,long revision=3,Guid? actor=null,Guid? scope=null)=>
        Playbooks(s=>s.WithdrawForSecurityAsync(actor??f.Platform,scope,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Aviso de seguridad",revision)));
    private Task<ExecutionSecurityState> HoldState(ExecutionFixture f,ExecutionItem e,Guid? actor=null)=>
        Executions(s=>s.SecurityStateAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id));
    private Task<ExecutionItem> DecideHold(ExecutionFixture f,ExecutionItem e,Guid hold,bool proceed=true,Guid? actor=null)=>
        Executions(s=>s.DecideSecurityHoldAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,hold,new(proceed,e.Revision,e.IncidentRevision,"Revision administrativa del bloqueo")));

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task SecurityWithdrawalFreezesProgressAndCurrentHoldReviewRestoresOnlyExecutionControls()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        var h=await HoldVersion(f);var state=await HoldState(f,e);Assert.Equal(h.Id,state.CurrentHold!.Id);Assert.False(state.ContinuityAuthorized);Assert.Equal("ARCHIVED",state.VersionStatus);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress),"execution_version_withdrawn");
        await Rejected(409,()=>Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Sin bloqueo explicito"))),"security_hold_decision_required");
        e=await DecideHold(f,e,h.Id);Assert.True((await HoldState(f,e)).ContinuityAuthorized);
        e=await FinishAllTasks(f,e);e=await Executions(s=>s.CompleteAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Completar")));
        Assert.Equal("COMPLETED",e.Status);Assert.Equal("IN_RESPONSE",(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Status);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task NewHoldInvalidatesPreviousAuthorizationWithoutOverwritingHistory()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);var first=await HoldVersion(f);e=await DecideHold(f,e,first.Id);
        var second=await HoldVersion(f,4);Assert.NotEqual(first.Id,second.Id);Assert.False((await HoldState(f,e)).ContinuityAuthorized);
        await Rejected(409,()=>DecideHold(f,e,first.Id),"security_hold_obsolete");await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress));
        e=await DecideHold(f,e,second.Id);var history=await Executions(s=>s.SecurityDecisionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(new[]{first.Id,second.Id},history.Select(d=>d.SecurityHoldId));
        e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);Assert.Equal("IN_PROGRESS",e.Tasks[0].Status);
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationRequiresCurrentHoldAndCanFollowAContinuityDecision(bool authorizeFirst)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);if(authorizeFirst)e=await DecideHold(f,e,h.Id);
        e=await DecideHold(f,e,h.Id,false);Assert.Equal("CANCELLED",e.Status);Assert.All(e.Tasks,t=>Assert.Equal("PENDING",t.Status));
        var state=await HoldState(f,e);Assert.False(state.ContinuityAuthorized);Assert.Equal(authorizeFirst?2:1,state.Decisions.Count);
        await Rejected(409,()=>DecideHold(f,e,h.Id));
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData("platform")] [InlineData("viewer")] [InlineData("analyst")] [InlineData("suspended")] [InlineData("locked")]
    public async Task ContinuityRequiresCurrentOrganizationAdminWithoutGlobalBypass(string role)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        if(role!="platform")
        {
            var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role=="viewer"?OrganizationRole.Viewer:role=="analyst"?OrganizationRole.Analyst:OrganizationRole.OrgAdmin)));
            if(role=="suspended")await Act(s=>s.ChangeMemberAsync(f.Admin,f.Org,member.Id,new(OrganizationRole.OrgAdmin,MembershipStatus.Suspended,member.UpdatedAt)));
            if(role=="locked")await InScope(async(_,db)=>{(await db.Users.SingleAsync(u=>u.Id==f.Platform)).LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);return await db.SaveChangesAsync();});
        }
        await Rejected(403,()=>DecideHold(f,e,h.Id,actor:f.Platform));Assert.False((await HoldState(f,e)).ContinuityAuthorized);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task SourceWithdrawalAuthorityAndPrivateScopeRemainSeparate()
    {
        var f=await ExecutionSeed(privatePlaybook:true);var e=await BeginExecution(f);
        await Rejected(403,()=>HoldVersion(f,actor:f.Platform,scope:f.Org));
        var h=await HoldVersion(f,actor:f.Admin,scope:f.Org);Assert.Equal(h.Id,(await HoldState(f,e)).CurrentHold!.Id);
        await Rejected(409,()=>HoldVersion(f,actor:f.Admin,scope:f.Org));
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task OrdinaryWithdrawalDoesNotImplicitlyGrantSecurityContinuity()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);await Playbooks(s=>s.WithdrawAsync(f.Platform,null,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Retirada ordinaria",3)));
        Assert.Null((await HoldState(f,e)).CurrentHold);await Rejected(409,()=>DecideHold(f,e,Guid.NewGuid()),"security_hold_obsolete");
        e=await Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Cancelar")));Assert.Equal("CANCELLED",e.Status);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task PublicHoldAppliesAcrossOrganizationsButContinuityIsExecutionScoped()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var beta=await Act(s=>s.CreateOrganizationAsync(f.Platform,new("Beta","beta","CL",Account("beta"))));
        var member=await InScope((_,db)=>db.OrganizationMemberships.SingleAsync(m=>m.OrganizationId==beta.Id));var incident=await RegisterIncident(member.UserId,beta.Id);
        incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.UnderEvaluation);incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.InResponse);
        var other=new ExecutionFixture(f.Platform,member.UserId,member.Id,beta.Id,incident,f.Playbook);var b=await BeginExecution(other);var h=await HoldVersion(f);
        Assert.Equal(h.Id,(await HoldState(other,b)).CurrentHold!.Id);e=await DecideHold(f,e,h.Id);Assert.True((await HoldState(f,e)).ContinuityAuthorized);Assert.False((await HoldState(other,b)).ContinuityAuthorized);
        await Rejected(403,()=>DecideHold(f,e,h.Id,actor:member.UserId));
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData("hold")] [InlineData("decision")]
    public async Task SecurityHistoryIsAppendOnlyAndDowngradeCannotLoseIt(string kind)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);if(kind=="decision")e=await DecideHold(f,e,h.Id);
        var table=kind=="hold"?"app.playbook_security_holds":"app.execution_security_decisions";
        foreach(var sql in new[]{"UPDATE "+table+" SET reason='Alterado'","DELETE FROM "+table,kind=="hold"?"TRUNCATE app.playbook_security_holds,app.execution_security_decisions":"TRUNCATE app.execution_security_decisions"})Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)))).SqlState);
        await InScope(async(_,db)=>{if(kind=="hold")(await db.PlaybookSecurityHolds.SingleAsync()).Reason="Alterado";else(await db.ExecutionSecurityDecisions.SingleAsync()).Reason="Alterado";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261027024554_DependencyExceptionRevocations");return true;}));
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentDecisionsAndNewWithdrawalNeverReuseOldHold(bool rotate)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        async Task<int> Decide(){try{await DecideHold(f,e,h.Id);return 200;}catch(IdentityOperationException ex){Assert.Equal(409,ex.StatusCode);return 409;}}
        async Task<int> Second(){if(rotate){await HoldVersion(f,4);return 200;}return await Decide();}
        var result=await Race(Decide,Second);if(rotate)Assert.False((await HoldState(f,e)).ContinuityAuthorized);else Assert.Single(result,x=>x==200);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task HoldDecisionHttpRequiresExplicitChoiceRevisionsCsrfAndNoStoreRead()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);var root=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}";
        using var anon=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(root+"/security-hold")).StatusCode);
        using var admin=Client();await Login(admin,"alpha");var route=root+$"/security-holds/{h.Id}/decision";
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new{expectedExecutionRevision=e.Revision,expectedIncidentRevision=e.IncidentRevision,reason="Falta decision"})).StatusCode);
        var csrf=admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new DecideSecurityHold(true,e.Revision,e.IncidentRevision,"Sin CSRF"))).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route,new DecideSecurityHold(true,e.Revision,e.IncidentRevision,"Continuidad revisada"))).StatusCode);
        using var response=await admin.GetAsync(root+"/security-hold");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task SqlAlsoFreezesRunningTasksAndContinuityDoesNotRemoveStepApprovalOrEvidence()
    {
        var f=await ExecutionSeed(customize:c=>c with{Steps=c.Steps.Select((s,i)=>s with{RequiresApproval=i==0,RequiresEvidence=i==0}).ToList()});var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        var h=await HoldVersion(f);e=await DecideHold(f,e,h.Id);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress),"task_approval_required");
        e=await AskStep(f,e);e=await DecideStep(f,e,await LastStepRequest(f,e));e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.Completed),"task_evidence_required");
        var next=await HoldVersion(f,4);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlAsync($"UPDATE app.execution_tasks SET status='COMPLETED',changed_at_revision=changed_at_revision+1 WHERE id={e.Tasks[0].Id}")))).SqlState);
        Assert.Equal("IN_PROGRESS",(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Tasks[0].Status);
        e=await DecideHold(f,e,next.Id);var evidence=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Confidential,"Prueba","Resultado",e.Id,e.Tasks[0].Id,e.IncidentRevision,"Registrar")));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await Executions(s=>s.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Completar",evidence.Evidence.Id)));Assert.Equal("COMPLETED",e.Tasks[0].Status);
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData(true)] [InlineData(false)]
    public async Task HoldDecisionRequiresBothCurrentRevisions(bool execution)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        await Rejected(409,()=>Executions(s=>s.DecideSecurityHoldAsync(f.Admin,f.Org,f.Incident.Id,e.Id,h.Id,new(true,execution?e.Revision-1:e.Revision,execution?e.IncidentRevision:e.IncidentRevision-1,"Revisiones"))));
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData("organization")] [InlineData("incident")] [InlineData("execution")] [InlineData("hold")]
    public async Task HoldDecisionsCannotCrossAuthorizedScope(string invalid)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        await Rejected(invalid=="organization"?403:invalid=="hold"?409:404,()=>Executions(s=>s.DecideSecurityHoldAsync(f.Admin,invalid=="organization"?Guid.NewGuid():f.Org,
            invalid=="incident"?Guid.NewGuid():f.Incident.Id,invalid=="execution"?Guid.NewGuid():e.Id,invalid=="hold"?Guid.NewGuid():h.Id,new(true,e.Revision,e.IncidentRevision,"Ambito"))));
    }

    [Theory,Trait("Feature","SecurityHolds")]
    [InlineData("audit")] [InlineData("events")] [InlineData("decisions")]
    public async Task FailedSecurityDecisionRollsBackCancellationAndBothHistories(string sink)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        var table=sink=="audit"?"audit.audit_events":sink=="events"?"app.execution_task_events":"app.execution_security_decisions";
        var sql="CREATE FUNCTION app.fail_hold_test() RETURNS trigger LANGUAGE plpgsql AS $b$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $b$; CREATE TRIGGER fail_hold_test BEFORE INSERT ON "+table+" FOR EACH ROW "+(sink=="audit"?"WHEN(NEW.outcome='SUCCESS') ":"")+"EXECUTE FUNCTION app.fail_hold_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql));await Assert.ThrowsAsync<DbUpdateException>(()=>DecideHold(f,e,h.Id,false));
        var current=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(e.Revision,current.Revision);Assert.Equal(e.IncidentRevision,current.IncidentRevision);Assert.Equal("IN_PROGRESS",current.Status);Assert.Empty((await HoldState(f,e)).Decisions);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task FailedSourceAuditDoesNotWithdrawVersionOrLeaveAPartialHold()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync("CREATE FUNCTION app.fail_source_hold() RETURNS trigger LANGUAGE plpgsql AS $b$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $b$; CREATE TRIGGER fail_source_hold BEFORE INSERT ON audit.audit_events FOR EACH ROW WHEN(NEW.outcome='SUCCESS') EXECUTE FUNCTION app.fail_source_hold();"));
        await Assert.ThrowsAsync<DbUpdateException>(()=>HoldVersion(f));Assert.Null((await HoldState(f,e)).CurrentHold);Assert.Equal("PUBLISHED",(await HoldState(f,e)).VersionStatus);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task SqlDecisionWithoutAtomicEventsCannotAuthorizeContinuity()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var h=await HoldVersion(f);
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlAsync($"UPDATE app.playbook_executions SET revision=revision+1 WHERE id={e.Id}");await db.Database.ExecuteSqlAsync($"UPDATE app.incidents SET revision=revision+1 WHERE id={f.Incident.Id}");
            db.ExecutionSecurityDecisions.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,VersionId=e.VersionId,ExecutionId=e.Id,SecurityHoldId=h.Id,Action="CONTINUE",ExecutionRevision=e.Revision+1,IncidentRevision=e.IncidentRevision+1,ActorUserId=f.Admin,CorrelationId=Guid.NewGuid(),Reason="Sin eventos"});
            await db.SaveChangesAsync();await tx.CommitAsync();return true;
        }));Assert.Equal("23514",ex.SqlState);Assert.False((await HoldState(f,e)).ContinuityAuthorized);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task RuntimeCredentialsCanWithdrawAndDecideWithoutMutableSecurityHistory()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var role="openirp_hold_runtime_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var b=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));await using var owner=new NpgsqlConnection(b.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try
        {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grant=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grant.ExecuteNonQueryAsync();
            b.Username=role;b.Password=password;await using var app=new TestApplication(b.ConnectionString);using var scope=app.Services.CreateScope();
            var source=scope.ServiceProvider.GetRequiredService<OpenIRP.Application.Playbooks.IPlaybookDraftService>();var h=await source.WithdrawForSecurityAsync(f.Platform,null,f.Playbook.Playbook.Id,e.VersionId,new("Seguridad runtime",3));
            using var decisionScope=app.Services.CreateScope();var service=decisionScope.ServiceProvider.GetRequiredService<IExecutionService>();e=await service.DecideSecurityHoldAsync(f.Admin,f.Org,f.Incident.Id,e.Id,h.Id,new(true,e.Revision,e.IncidentRevision,"Continuar runtime"));
            using var readScope=app.Services.CreateScope();Assert.True((await readScope.ServiceProvider.GetRequiredService<IExecutionService>().SecurityStateAsync(f.Admin,f.Org,f.Incident.Id,e.Id)).ContinuityAuthorized);
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }

    private sealed class HoldSnapshotInterceptor(Func<Task> change):DbCommandInterceptor
    {
        private int calls;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {
            if((command.CommandText.Contains("playbook_security_holds",StringComparison.Ordinal)||command.CommandText.Contains("effective_security_hold",StringComparison.Ordinal))&&Interlocked.Exchange(ref calls,1)==0)await change();return result;
        }
    }
    [Fact,Trait("Feature","SecurityHolds")]
    public async Task ConcurrentWithdrawalDoesNotMixPublishedVersionWithNewHoldInAReadSnapshot()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var interceptor=new HoldSnapshotInterceptor(async()=>{await HoldVersion(f);});
        var connection=await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString()!));await using var app=new ReadinessApplication(connection,interceptor);using var scope=app.Services.CreateScope();
        var state=await scope.ServiceProvider.GetRequiredService<IExecutionService>().SecurityStateAsync(f.Admin,f.Org,f.Incident.Id,e.Id);Assert.Null(state.CurrentHold);Assert.Equal("PUBLISHED",state.VersionStatus);
        Assert.NotNull((await HoldState(f,e)).CurrentHold);
    }

    [Fact,Trait("Feature","SecurityHolds")]
    public async Task EmptySecurityMigrationCanRoundTripWithoutChangingPublishedExecution()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261027024554_DependencyExceptionRevocations");await db.Database.MigrateAsync();return true;});
        e=await AssignTask(f,e,e.Tasks[0].Id);e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);Assert.Null((await HoldState(f,e)).CurrentHold);
    }
}
