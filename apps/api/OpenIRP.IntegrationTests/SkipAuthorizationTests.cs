using Microsoft.Extensions.DependencyInjection;
using System.Net;
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
    private Task<ExecutionItem> AskSkip(ExecutionFixture f, ExecutionItem e, Guid? actor = null, Guid? task = null,
        string impact = "Impacto de no ejecutar documentado", string risk = "Riesgo residual aceptable para revisiÃƒÂ³n") =>
        Executions(s => s.RequestSkipAsync(actor ?? f.Admin, f.Org, f.Incident.Id, e.Id, task ?? e.Tasks[0].Id,
            new(impact, risk, e.Revision, e.IncidentRevision, "Solicitar omisiÃƒÂ³n excepcional")));
    private async Task<Guid> LastSkipRequest(ExecutionFixture f, ExecutionItem e, Guid? task = null) =>
        (await Executions(s => s.SkipAuthorizationsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, task ?? e.Tasks[0].Id))).Last(a => a.Action == "REQUESTED").Id;
    private Task<ExecutionItem> DecideSkip(ExecutionFixture f, ExecutionItem e, Guid request, bool approve = true,
        Guid? actor = null, Guid? task = null, bool self = true, string? justification = "ExcepciÃƒÂ³n explÃƒÂ­cita del administrador ÃƒÂºnico") =>
        Executions(s => s.DecideSkipAsync(actor ?? f.Admin, f.Org, f.Incident.Id, e.Id, task ?? e.Tasks[0].Id, request,
            new(approve, self, justification, e.Revision, e.IncidentRevision, "DecisiÃƒÂ³n de omisiÃƒÂ³n revisada")));

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task SkipApprovalIsAtomicScopedAndDoesNotSatisfyDependenciesOrStepApproval()
    {
        var f = await ExecutionSeed(approvals:true, approvalEvidence:true); var e = await BeginExecution(f); var task=e.Tasks[0].Id;
        var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,task,member.Id);
        e=await AskSkip(f,e,f.Platform); var id=await LastSkipRequest(f,e);
        await Rejected(403,()=>DecideSkip(f,e,id,actor:f.Platform));
        e=await DecideSkip(f,e,id,self:false,justification:null);
        Assert.Equal("SKIPPED",e.Tasks[0].Status); Assert.Null(e.Tasks[0].CompletionEvidenceId);
        Assert.Empty(await Executions(s=>s.ApprovalsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task)));
        var history=await Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task));
        Assert.Equal(new[]{"REQUESTED","APPROVED"},history.Select(a=>a.Action));
        Assert.Equal(history[0].Id,history[1].RequestId); Assert.Equal(history[0].Impact,history[1].Impact);
        Assert.NotEmpty(history[0].DependencyContext.GetProperty("successors").EnumerateArray());
        var events=await Executions(s=>s.EventsAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        foreach(var row in history) { var ev=Assert.Single(events,a=>a.Revision==row.ExecutionRevision); Assert.Equal(row.CorrelationId,ev.CorrelationId); Assert.Equal(row.IncidentRevision,ev.IncidentRevision); }
        await Rejected(409,()=>AssignTask(f,e,task),"execution_task_is_terminal");
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.InProgress,f.Platform));
        var next=e.Tasks[1].Id; e=await AssignTask(f,e,next);
        await Rejected(409,()=>TaskState(f,e,next,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
        await Rejected(409,()=>Executions(s=>s.CompleteAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"No completada"))));
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData(false,null)] [InlineData(true,null)] [InlineData(true," ")]
    public async Task SkipSelfApprovalRequiresExplicitJustifiedException(bool self,string? reason)
    {
        var f=await ExecutionSeed(); var e=await BeginExecution(f); e=await AssignTask(f,e,e.Tasks[0].Id); e=await AskSkip(f,e); var id=await LastSkipRequest(f,e);
        await Rejected(400,()=>DecideSkip(f,e,id,self:self,justification:reason),"invalid_skip_self_approval");
        Assert.Equal("PENDING",(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Tasks[0].Status);
        e=await DecideSkip(f,e,id); Assert.Equal("SKIPPED",e.Tasks[0].Status);
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("impact")] [InlineData("risk")] [InlineData("long-impact")] [InlineData("long-risk")]
    public async Task SkipRequiresBoundedImpactAndResidualRisk(string field)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        await Rejected(400,()=>AskSkip(f,e,impact:field=="impact"?" ":field=="long-impact"?new string('x',2001):"Impacto",
            risk:field=="risk"?" ":field=="long-risk"?new string('x',2001):"Riesgo"));
        Assert.Empty(await Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id)));
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("same-assignment")] [InlineData("other-task")] [InlineData("incident")] [InlineData("task-state")]
    public async Task SkipDecisionInvalidatedByAnyInterveningContextChange(string change)
    {
        var f=await ExecutionSeed(); var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);e=await AskSkip(f,e);var id=await LastSkipRequest(f,e);
        if(change=="same-assignment")e=await AssignTask(f,e,e.Tasks[0].Id);
        else if(change=="other-task")e=await AssignTask(f,e,e.Tasks[1].Id);
        else if(change=="task-state")e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.Blocked);
        else { var current=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id)); await Incidents(s=>s.AssignAsync(f.Admin,f.Org,f.Incident.Id,new(f.Membership,"Contexto actualizado",current.Revision))); e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id)); }
        await Rejected(409,()=>DecideSkip(f,e,id),"skip_authorization_context_changed");
        e=await AskSkip(f,e);var fresh=await LastSkipRequest(f,e);Assert.NotEqual(id,fresh);
        e=await DecideSkip(f,e,fresh);Assert.Equal("SKIPPED",e.Tasks[0].Status);
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task SkipRejectionAllowsFreshRequestAndCannotBeOverwritten()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);e=await AskSkip(f,e);var id=await LastSkipRequest(f,e);
        await Rejected(409,()=>AskSkip(f,e),"skip_authorization_already_pending");
        e=await DecideSkip(f,e,id,approve:false,self:false,justification:null);
        await Rejected(409,()=>DecideSkip(f,e,id),"skip_authorization_already_decided");
        Assert.Equal("PENDING",e.Tasks[0].Status);
        e=await AskSkip(f,e);var fresh=await LastSkipRequest(f,e);
        await Rejected(409,()=>DecideSkip(f,e,id),"skip_authorization_request_obsolete");
        e=await DecideSkip(f,e,fresh);Assert.Equal("SKIPPED",e.Tasks[0].Status);
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("organization")] [InlineData("assignee")] [InlineData("account")] [InlineData("lockout")] [InlineData("withdrawn")] [InlineData("cancelled")]
    public async Task SkipDecisionRechecksParticipantsAndExecution(string invalid)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,e.Tasks[0].Id,member.Id);e=await AskSkip(f,e,f.Platform);var id=await LastSkipRequest(f,e);
        if(invalid=="organization") await InScope(async(_,db)=>{(await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;return await db.SaveChangesAsync();});
        else if(invalid=="assignee") await Act(s=>s.ChangeMemberAsync(f.Admin,f.Org,member.Id,new(OrganizationRole.Analyst,MembershipStatus.Suspended,member.UpdatedAt)));
        else if(invalid is "account" or "lockout") await InScope(async(_,db)=>{var u=await db.Users.SingleAsync(u=>u.Id==f.Platform);if(invalid=="account")u.IsActive=false;else u.LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);return await db.SaveChangesAsync();});
        else if(invalid=="withdrawn") await Playbooks(s=>s.WithdrawAsync(f.Platform,null,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Retirada",3)));
        else e=await Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Cancelada")));
        await Rejected(invalid=="organization"?403:409,()=>DecideSkip(f,e,id,self:false,justification:null));
        Assert.Equal(1,await InScope((_,db)=>db.SkipAuthorizations.CountAsync()));
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("request","audit")] [InlineData("decision","audit")] [InlineData("decision","events")] [InlineData("request","skip")] [InlineData("decision","skip")]
    public async Task SkipFailuresRollBackStateHistoryAndBothRevisions(string operation,string sink)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        Guid id=Guid.Empty;if(operation=="decision"){e=await AskSkip(f,e);id=await LastSkipRequest(f,e);}
        var before=await InScope((_,db)=>db.SkipAuthorizations.CountAsync());
        var table=sink=="audit"?"audit.audit_events":sink=="events"?"app.execution_task_events":"app.skip_authorizations";
        var script="CREATE FUNCTION app.fail_skip_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $body$; CREATE TRIGGER fail_skip_test BEFORE INSERT ON "+table+" FOR EACH ROW "+(sink=="audit"?"WHEN (NEW.outcome='SUCCESS') ":"")+"EXECUTE FUNCTION app.fail_skip_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(script));
        await Assert.ThrowsAsync<DbUpdateException>(async()=>{if(operation=="request")await AskSkip(f,e);else await DecideSkip(f,e,id);});
        var after=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal(e.Revision,after.Revision);Assert.Equal(e.IncidentRevision,after.IncidentRevision);Assert.Equal("PENDING",after.Tasks[0].Status);
        Assert.Equal(before,await InScope((_,db)=>db.SkipAuthorizations.CountAsync()));
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("update")] [InlineData("delete")] [InlineData("truncate")]
    public async Task SkipHistoryIsAppendOnlyInSqlAndEf(string operation)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);e=await AskSkip(f,e);
        var sql=operation=="update"?"UPDATE app.skip_authorizations SET reason='Alterado'":operation=="delete"?"DELETE FROM app.skip_authorizations":"TRUNCATE app.skip_authorizations,app.dependency_exceptions,app.dependency_exception_revocations";
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));Assert.Equal("42501",error.SqlState);
        await InScope(async(_,db)=>{var row=await db.SkipAuthorizations.SingleAsync();row.Impact="Alterado";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentSkipRequestsAndDecisionsHaveOneWinner(bool decision)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);Guid id=Guid.Empty;
        if(decision){e=await AskSkip(f,e);id=await LastSkipRequest(f,e);}
        async Task<bool> Attempt(){try{if(decision)await DecideSkip(f,e,id);else await AskSkip(f,e);return true;}catch(IdentityOperationException ex){Assert.Equal(409,ex.StatusCode);return false;}}
        var outcomes=await Task.WhenAll(Attempt(),Attempt());Assert.Single(outcomes,x=>x);
        Assert.Equal(decision?2:1,await InScope((_,db)=>db.SkipAuthorizations.CountAsync()));
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task SkipScopeRolesPaginationAndStaleRevisionsAreEnforced()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var task=e.Tasks[0].Id;
        await Rejected(403,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Platform,f.Org,f.Incident.Id,e.Id,task)));
        await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Viewer)));
        Assert.Empty(await Executions(s=>s.SkipAuthorizationsAsync(f.Platform,f.Org,f.Incident.Id,e.Id,task)));
        await Rejected(403,()=>AskSkip(f,e,f.Platform));
        await Rejected(404,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,Guid.NewGuid(),e.Id,task)));
        await Rejected(404,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,Guid.NewGuid(),task)));
        await Rejected(404,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,Guid.NewGuid())));
        await Rejected(400,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,-1)));
        await Rejected(400,()=>Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,0,101)));
        e=await AssignTask(f,e,task);var stale=e;e=await AskSkip(f,e);
        await Rejected(409,()=>AskSkip(f,stale),"stale_incident_revision");
        await Rejected(409,()=>Executions(s=>s.RequestSkipAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new("Impacto","Riesgo",stale.Revision,e.IncidentRevision,"Obsoleto"))),"stale_execution_revision");
        Assert.Single(await Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,0,1)));
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task SqlRejectsSkipWithoutAuthorizationAndHistoryLoss()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);var task=e.Tasks[0].Id;
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlAsync($"UPDATE app.execution_tasks SET status='SKIPPED',changed_at_revision=changed_at_revision+1 WHERE id={task}")));Assert.Equal("23514",error.SqlState);
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026183812_PrivateEvidenceFiles");await db.Database.MigrateAsync();return true;});
        e=await AskSkip(f,e);
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026183812_PrivateEvidenceFiles");return true;}));
        await InScope(async(_,db)=>{await db.Database.MigrateAsync();return true;});
        Assert.Single(await Executions(s=>s.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task)));
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task EverySkippedTaskNeedsIndependentDecisionAndExecutionCompletionDoesNotResolveIncident()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        foreach(var t in e.Tasks.ToArray()){e=await AssignTask(f,e,t.Id);e=await AskSkip(f,e,task:t.Id);e=await DecideSkip(f,e,await LastSkipRequest(f,e,t.Id),task:t.Id);}
        Assert.All(e.Tasks,t=>Assert.Equal("SKIPPED",t.Status));
        e=await Executions(s=>s.CompleteAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Finalizada con omisiones explÃƒÂ­citas")));
        Assert.Equal("COMPLETED",e.Status);Assert.Equal("IN_RESPONSE",(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Status);
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task HttpSkipRequiresCsrfAuthenticationAndExplicitDecisionFlags()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        var route=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[0].Id}/skip-authorizations";
        using var anon=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(route)).StatusCode);
        using var admin=Client(); await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route,new RequestSkipAuthorization("Impacto","Riesgo",e.Revision,e.IncidentRevision,"OmisiÃƒÂ³n HTTP"))).StatusCode);
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));var id=await LastSkipRequest(f,e);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route+$"/{id}/decision",new{expectedExecutionRevision=e.Revision,expectedIncidentRevision=e.IncidentRevision,reason="Faltan flags"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route+$"/{id}/decision",new DecideSkipAuthorization(true,true,"ExcepciÃƒÂ³n",e.Revision,e.IncidentRevision,"Autorizar"))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new RequestSkipAuthorization("Impacto","Riesgo",e.Revision,e.IncidentRevision,"Sin CSRF"))).StatusCode);
    }
    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task RestrictedRuntimeCanRequestAndApproveSkipsWithoutRewritingDecisions()
    {
        var f = await ExecutionSeed(approvals: true); var role = "openirp_execution_runtime_" + Guid.NewGuid().ToString("N");
        var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role', @role, false)", owner))
            { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using (var runtime = new TestApplication(builder.ConnectionString))
            {
                using var scope = runtime.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IExecutionService>();
                var current = await service.StartAsync(f.Admin, f.Org, f.Incident.Id, new(f.Playbook.Playbook.Id, f.Playbook.VersionId, 3, 3, "Runtime start"));
                var task = current.Tasks[0].Id;
                current = await service.AssignAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(f.Membership, 1, 4, "Runtime assign"));
                current = await service.RequestSkipAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task,new("Impacto runtime","Riesgo runtime",current.Revision,current.IncidentRevision,"Runtime request"));
                var request = (await service.SkipAuthorizationsAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task)).Single().Id;
                current = await service.DecideSkipAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task,request,new(true,true,"ExcepciÃ³n runtime explÃ­cita",current.Revision,current.IncidentRevision,"Runtime approve"));
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach (var sql in new[] { "UPDATE app.execution_tasks SET step_id=gen_random_uuid()", "DELETE FROM app.execution_task_events", "DELETE FROM app.playbook_executions", "UPDATE app.skip_authorizations SET reason='Alterado'", "DELETE FROM app.skip_authorizations" })
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
                    Assert.Equal("42501", error.SqlState);
                }
                var cancelled = await service.CancelAsync(f.Admin, f.Org, f.Incident.Id, current.Id, new(current.Revision, current.IncidentRevision, "Runtime cancel"));
                Assert.Equal("CANCELLED", cancelled.Status);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Theory, Trait("Feature", "SkipAuthorizations")]
    [InlineData("unassigned")] [InlineData("in-progress")] [InlineData("completed")]
    public async Task SkipRequestRequiresAssignedUnstartedOrBlockedTask(string state)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var task=e.Tasks[0].Id;
        if(state!="unassigned") {e=await AssignTask(f,e,task);e=await TaskState(f,e,task,ExecutionTaskState.InProgress);}
        if(state=="completed") e=await TaskState(f,e,task,ExecutionTaskState.Completed);
        await Rejected(409,()=>AskSkip(f,e),state=="unassigned"?"execution_task_unassigned":"skip_requires_unstarted_or_blocked_task");
        Assert.Equal(0,await InScope((_,db)=>db.SkipAuthorizations.CountAsync()));
    }

    [Fact, Trait("Feature", "SkipAuthorizations")]
    public async Task DatabaseRejectsUnloggedSkipRequestAndProtectsTerminalSkip()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var task=e.Tasks[0].Id;e=await AssignTask(f,e,task);
        var exception=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async(_,db)=>
        {
            db.SkipAuthorizations.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,VersionId=e.VersionId,ExecutionId=e.Id,
                TaskId=task,AssignedMembershipId=f.Membership,ExecutionRevision=e.Revision+1,IncidentRevision=e.IncidentRevision+1,
                ActorUserId=f.Admin,Reason="Sin historia",Impact="Impacto",ResidualRisk="Riesgo"});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        e=await AskSkip(f,e);e=await DecideSkip(f,e,await LastSkipRequest(f,e));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlAsync($"UPDATE app.execution_tasks SET status='PENDING',changed_at_revision=changed_at_revision+1 WHERE id={task}")));
        Assert.Equal("23514",error.SqlState);
    }
}
