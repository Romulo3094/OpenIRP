using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<(ExecutionFixture F, ExecutionItem E, Guid Approval)> RevocationSeed()
    {
        var (f,e)=await DependencySeed();e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        var approval=(await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id))).Single(a=>a.Action=="APPROVED").Id;
        return(f,e,approval);
    }
    private Task<ExecutionItem> RevokeDependency(ExecutionFixture f,ExecutionItem e,Guid approval,Guid? actor=null,string reason="Revocar autorización tras revisión") =>
        Executions(s=>s.RevokeDependencyExceptionAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id,approval,new(e.Revision,e.IncidentRevision,reason)));
    private Task<DependencyRevocationItem> ReadRevocation(ExecutionFixture f,ExecutionItem e,Guid approval,Guid? actor=null) =>
        Executions(s=>s.DependencyRevocationAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id,approval));

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task RevocationPreservesApprovalAndHistoryAndRequiresFreshIndependentReview()
    {
        var(f,e,approval)=await RevocationSeed();
        await Rejected(404,()=>ReadRevocation(f,e,approval),"dependency_revocation_not_found");
        var before=e;e=await RevokeDependency(f,e,approval);var row=await ReadRevocation(f,e,approval);
        Assert.Equal(approval,row.ApprovalId);Assert.False(row.BlockedTask);Assert.Equal(before.Revision+1,e.Revision);Assert.Equal(before.IncidentRevision+1,e.IncidentRevision);
        var report=await DependencyReadiness(f,e);var edge=Assert.Single(report.Dependencies);
        Assert.False(report.AllDependenciesSatisfied);Assert.Equal("APPROVED",edge.LatestDecisionAction);Assert.Equal(row.Id,edge.RevocationId);
        Assert.Equal(row.Reason,edge.RevocationReason);Assert.Equal(row.OccurredAt,edge.RevokedAt);
        Assert.Equal(2,await InScope((_,db)=>db.DependencyExceptions.CountAsync()));
        var events=await Executions(s=>s.EventsAsync(f.Admin,f.Org,f.Incident.Id,e.Id));var ev=Assert.Single(events,x=>x.Action=="DEPENDENCY_EXCEPTION_REVOKED");
        Assert.Equal(row.CorrelationId,ev.CorrelationId);Assert.Equal(row.ExecutionRevision,ev.Revision);Assert.Equal(row.IncidentRevision,ev.IncidentRevision);
        Assert.Equal(1,await InScope((_,db)=>db.AuditEvents.CountAsync(a=>a.CorrelationId==row.CorrelationId && a.Action=="incident.dependency_exception_revoked" && a.Outcome=="SUCCESS")));
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
        await Rejected(409,()=>RevokeDependency(f,e,approval),"dependency_exception_already_revoked");
        e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        Assert.True((await DependencyReadiness(f,e)).AllDependenciesSatisfied);
        Assert.Null(Assert.Single((await DependencyReadiness(f,e)).Dependencies).RevocationId);
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);Assert.Equal("IN_PROGRESS",e.Tasks[1].Status);
        Assert.Equal(row.Id,(await ReadRevocation(f,e,approval)).Id);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task RevokingRunningTaskBlocksItAtomicallyWithoutChangingAssigneeOrPublishedDag()
    {
        var(f,e,approval)=await RevocationSeed();e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        e=await RevokeDependency(f,e,approval);Assert.Equal("BLOCKED",e.Tasks[1].Status);Assert.Equal(f.Membership,e.Tasks[1].AssignedMembershipId);
        Assert.Equal(e.Revision,e.Tasks[1].ChangedAtRevision);Assert.True((await ReadRevocation(f,e,approval)).BlockedTask);
        Assert.Equal("SKIPPED",e.Tasks[0].Status);Assert.Equal(2,e.Dependencies.Count);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.Completed));
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("completed-task")] [InlineData("cancelled-run")] [InlineData("withdrawn-version")]
    public async Task RevocationCannotRewriteTerminalHistoryOrBypassVersionWithdrawal(string state)
    {
        var(f,e,approval)=await RevocationSeed();
        if(state=="completed-task"){e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.Completed);}
        else if(state=="cancelled-run")e=await Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Cancelar")));
        else await Playbooks(s=>s.WithdrawAsync(f.Platform,null,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Retirada",3)));
        await Rejected(409,()=>RevokeDependency(f,e,approval));Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync()));
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("profile")] [InlineData("assignment")] [InlineData("unassigned")]
    public async Task AdminCanRetireAnExpiredGrantWithoutRequiringAnEligibleCurrentAssignee(string change)
    {
        var(f,e,approval)=await RevocationSeed();
        if(change=="profile")await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,f.Playbook.Requirements[0].CapabilityId,new(CapabilityAvailability.Available,"Nuevo perfil",0)));
        else if(change=="assignment")e=await AssignTask(f,e,e.Tasks[1].Id);
        else e=await Executions(s=>s.AssignAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,new(null,e.Revision,e.IncidentRevision,"Desasignar")));
        e=await RevokeDependency(f,e,approval);Assert.False((await DependencyReadiness(f,e)).AllDependenciesSatisfied);
        Assert.False((await ReadRevocation(f,e,approval)).BlockedTask);
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("platform")] [InlineData("viewer")] [InlineData("analyst")] [InlineData("suspended-member")] [InlineData("inactive-account")] [InlineData("lockout")] [InlineData("suspended-organization")]
    public async Task OnlyCurrentEligibleOrganizationAdminsMayRevoke(string role)
    {
        var(f,e,approval)=await RevocationSeed();
        if(role!="platform")
        {
            var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role=="viewer"?OrganizationRole.Viewer:role=="analyst"?OrganizationRole.Analyst:OrganizationRole.OrgAdmin)));
            if(role=="suspended-member")await Act(s=>s.ChangeMemberAsync(f.Admin,f.Org,member.Id,new(OrganizationRole.OrgAdmin,MembershipStatus.Suspended,member.UpdatedAt)));
            else if(role is "inactive-account" or "lockout")await InScope(async(_,db)=>{var u=await db.Users.SingleAsync(u=>u.Id==f.Platform);if(role=="lockout")u.LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);else u.IsActive=false;return await db.SaveChangesAsync();});
            else if(role=="suspended-organization")await InScope(async(_,db)=>{(await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;return await db.SaveChangesAsync();});
        }
        await Rejected(403,()=>RevokeDependency(f,e,approval,actor:f.Platform));Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync()));
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("approval")] [InlineData("request")] [InlineData("task")] [InlineData("predecessor")] [InlineData("execution")] [InlineData("incident")] [InlineData("organization")]
    public async Task RevocationRequiresAnApprovalInTheExactAuthorizedScope(string missing)
    {
        var(f,e,approval)=await RevocationSeed();var request=await LastDependencyRequest(f,e);
        await Rejected(missing=="organization"?403:404,()=>Executions(s=>s.RevokeDependencyExceptionAsync(f.Admin,
            missing=="organization"?Guid.NewGuid():f.Org,missing=="incident"?Guid.NewGuid():f.Incident.Id,missing=="execution"?Guid.NewGuid():e.Id,
            missing=="task"?e.Tasks[2].Id:e.Tasks[1].Id,missing=="predecessor"?e.Tasks[2].Id:e.Tasks[0].Id,
            missing=="approval"?Guid.NewGuid():missing=="request"?request:approval,new(e.Revision,e.IncidentRevision,"Revisar"))));
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData(true)] [InlineData(false)]
    public async Task RevocationChecksBothOptimisticRevisionTokens(bool execution)
    {
        var(f,e,approval)=await RevocationSeed();await Rejected(409,()=>Executions(s=>s.RevokeDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id,approval,
            new(execution?e.Revision-1:e.Revision,execution?e.IncidentRevision:e.IncidentRevision-1,"Revision obsoleta"))));
        Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync()));
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData(0)] [InlineData(1)] [InlineData(2001)]
    public async Task RevocationRequiresABoundedNonBlankReason(int length)
    {
        var(f,e,approval)=await RevocationSeed();await Rejected(400,()=>RevokeDependency(f,e,approval,reason:length==1?" ":new string('x',length)));
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("update")] [InlineData("delete")] [InlineData("truncate")]
    public async Task RevocationHistoryIsAppendOnlyInSqlAndEf(string operation)
    {
        var(f,e,approval)=await RevocationSeed();e=await RevokeDependency(f,e,approval);
        var sql=operation=="update"?"UPDATE app.dependency_exception_revocations SET reason='Alterado'":operation=="delete"?"DELETE FROM app.dependency_exception_revocations":"TRUNCATE app.dependency_exception_revocations";
        Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)))).SqlState);
        await InScope(async(_,db)=>{var row=await db.DependencyExceptionRevocations.SingleAsync();if(operation=="delete")db.Remove(row);else row.Reason="Alterado";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData("audit")] [InlineData("events")] [InlineData("incidents")] [InlineData("revocations")]
    public async Task RevocationFailureRollsBackTheBlockRevisionsAndHistory(string sink)
    {
        var(f,e,approval)=await RevocationSeed();e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        var table=sink=="audit"?"audit.audit_events":sink=="events"?"app.execution_task_events":sink=="incidents"?"app.incident_events":"app.dependency_exception_revocations";
        var sql="CREATE FUNCTION app.fail_revocation_test() RETURNS trigger LANGUAGE plpgsql AS $b$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $b$; CREATE TRIGGER fail_revocation_test BEFORE INSERT ON "+table+" FOR EACH ROW "+(sink=="audit"?"WHEN(NEW.outcome='SUCCESS') ":"")+"EXECUTE FUNCTION app.fail_revocation_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql));
        await Assert.ThrowsAsync<DbUpdateException>(()=>RevokeDependency(f,e,approval));
        var after=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(e.Revision,after.Revision);Assert.Equal(e.IncidentRevision,after.IncidentRevision);Assert.Equal("IN_PROGRESS",after.Tasks[1].Status);
        Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync()));Assert.True((await DependencyReadiness(f,e)).AllDependenciesSatisfied);
    }

    [Theory,Trait("Feature","DependencyRevocations")]
    [InlineData(false)] [InlineData(true)]
    public async Task RevocationRacesSerializeWithDuplicateRevocationAndCompletion(bool completion)
    {
        var(f,e,approval)=await RevocationSeed();if(completion)e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        async Task<int> Attempt(bool finish){try{if(finish)await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.Completed);else await RevokeDependency(f,e,approval);return 200;}catch(IdentityOperationException ex){Assert.Equal(409,ex.StatusCode);return 409;}}
        var results=await Race(()=>Attempt(false),()=>Attempt(completion));Assert.Single(results,x=>x==200);Assert.Single(results,x=>x==409);
        var current=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));var count=await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync());
        if(count==1){Assert.Equal(completion?"BLOCKED":"PENDING",current.Tasks[1].Status);Assert.False((await DependencyReadiness(f,current)).AllDependenciesSatisfied);}
        else{Assert.True(completion);Assert.Equal("COMPLETED",current.Tasks[1].Status);}
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task SqlRejectsRevocationWithoutItsAtomicEvents()
    {
        var(f,e,approval)=await RevocationSeed();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync();var a=await db.DependencyExceptions.SingleAsync(a=>a.Id==approval);
            await db.Database.ExecuteSqlAsync($"UPDATE app.playbook_executions SET revision=revision+1 WHERE id={e.Id}");
            await db.Database.ExecuteSqlAsync($"UPDATE app.incidents SET revision=revision+1 WHERE id={f.Incident.Id}");
            db.DependencyExceptionRevocations.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,VersionId=e.VersionId,ExecutionId=e.Id,TaskId=e.Tasks[1].Id,
                DependencyId=a.DependencyId,PredecessorTaskId=a.PredecessorTaskId,SkipAuthorizationId=a.SkipAuthorizationId,AssignedMembershipId=a.AssignedMembershipId,
                ApprovalId=approval,ExecutionRevision=e.Revision+1,IncidentRevision=e.IncidentRevision+1,ActorUserId=f.Admin,CorrelationId=Guid.NewGuid(),Reason="Sin historia"});
            await db.SaveChangesAsync();await tx.CommitAsync();return true;
        }));
        Assert.Equal("23514",error.SqlState);Assert.Contains("matching atomic history",error.MessageText);
        Assert.True((await DependencyReadiness(f,e)).AllDependenciesSatisfied);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task RevocationMigrationCanUpgradeExistingApprovalsButCannotRemoveRevocationHistory()
    {
        var(f,e,approval)=await RevocationSeed();
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026224629_DependencyExceptions");await db.Database.MigrateAsync();return true;});
        Assert.True((await DependencyReadiness(f,e)).AllDependenciesSatisfied);e=await RevokeDependency(f,e,approval);
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026224629_DependencyExceptions");return true;}));
        Assert.Equal(approval,(await ReadRevocation(f,e,approval)).ApprovalId);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task HttpRevocationRequiresCsrfExplicitRevisionsAndExposesANoStoreReceipt()
    {
        var(f,e,approval)=await RevocationSeed();var route=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[1].Id}/dependencies/{e.Tasks[0].Id}/exceptions/{approval}/revocation";
        using var anon=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(route)).StatusCode);
        using var admin=Client();await Login(admin,"alpha");var csrf=admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new{reason="Faltan revisiones"})).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new ExecutionCommand(e.Revision,e.IncidentRevision,"Sin CSRF"))).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route,new ExecutionCommand(e.Revision,e.IncidentRevision,"Revocar por HTTP"))).StatusCode);
        using var receipt=await admin.GetAsync(route);Assert.Equal(HttpStatusCode.OK,receipt.StatusCode);Assert.True(receipt.Headers.CacheControl!.NoStore);
        Assert.Equal(approval,(await receipt.Content.ReadFromJsonAsync<DependencyRevocationItem>())!.ApprovalId);
        await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Viewer)));Assert.Equal(approval,(await ReadRevocation(f,e,approval,actor:f.Platform)).ApprovalId);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task AdminCanBlockAndRevokeEvenWhenTheAssignedAccountWasDisabled()
    {
        var(f,e)=await DependencySeed();var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,e.Tasks[1].Id,member.Id);e=await AskDependency(f,e,actor:f.Platform);
        e=await DecideDependency(f,e,await LastDependencyRequest(f,e),self:false,justification:null);
        var approval=(await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id))).Single(a=>a.Action=="APPROVED").Id;
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress,actor:f.Platform);
        await InScope(async(_,db)=>{(await db.Users.SingleAsync(u=>u.Id==f.Platform)).IsActive=false;return await db.SaveChangesAsync();});
        e=await RevokeDependency(f,e,approval);Assert.Equal("BLOCKED",e.Tasks[1].Status);Assert.True((await ReadRevocation(f,e,approval)).BlockedTask);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task RevokingOneEdgeDoesNotRevokeAnotherIndependentlyApprovedEdge()
    {
        var f=await ExecutionSeed(customize:c=>c with{Dependencies=[new(c.Steps[0].Code,c.Steps[2].Code),new(c.Steps[1].Code,c.Steps[2].Code)]});var e=await BeginExecution(f);
        foreach(var t in e.Tasks.Take(2).ToArray()){e=await AssignTask(f,e,t.Id);e=await AskSkip(f,e,task:t.Id);e=await DecideSkip(f,e,await LastSkipRequest(f,e,t.Id),task:t.Id);}
        var target=e.Tasks[2].Id;e=await AssignTask(f,e,target);
        foreach(var pred in e.Tasks.Take(2).Select(t=>t.Id).ToArray()){e=await AskDependency(f,e,target:target,predecessor:pred);e=await DecideDependency(f,e,await LastDependencyRequest(f,e,target,pred),target:target,predecessor:pred);}
        var approval=(await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,e.Tasks[0].Id))).Single(a=>a.Action=="APPROVED").Id;
        e=await Executions(s=>s.RevokeDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,e.Tasks[0].Id,approval,new(e.Revision,e.IncidentRevision,"Revocar solo primer vinculo")));
        var report=await DependencyReadiness(f,e,target);Assert.Equal(1,report.UnsatisfiedCount);
        Assert.Equal(ExecutionDependencySatisfaction.Unsatisfied,report.Dependencies[0].Satisfaction);Assert.NotNull(report.Dependencies[0].RevocationId);
        Assert.Equal(ExecutionDependencySatisfaction.ApprovedException,report.Dependencies[1].Satisfaction);Assert.Null(report.Dependencies[1].RevocationId);
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task ForeignOrganizationAdminCannotRevokeOrReadAnotherOrganizationsApproval()
    {
        var(f,e,approval)=await RevocationSeed();var beta=await Act(s=>s.CreateOrganizationAsync(f.Platform,new("Beta","beta","CL",Account("beta"))));
        var member=await InScope((_,db)=>db.OrganizationMemberships.SingleAsync(m=>m.OrganizationId==beta.Id));
        await Rejected(403,()=>RevokeDependency(f,e,approval,actor:member.UserId));await Rejected(403,()=>ReadRevocation(f,e,approval,actor:member.UserId));
        var incident=await RegisterIncident(member.UserId,beta.Id);incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.UnderEvaluation);incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.InResponse);
        var other=new ExecutionFixture(f.Platform,member.UserId,member.Id,beta.Id,incident,f.Playbook);var run=await BeginExecution(other);
        await Rejected(404,()=>Executions(s=>s.RevokeDependencyExceptionAsync(member.UserId,beta.Id,incident.Id,run.Id,run.Tasks[1].Id,run.Tasks[0].Id,approval,new(run.Revision,run.IncidentRevision,"ID ajeno"))));
        Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptionRevocations.CountAsync()));
    }

    [Fact,Trait("Feature","DependencyRevocations")]
    public async Task RestrictedRuntimeCanRevokeAndReadButCannotRewriteRevocations()
    {
        var(f,e,approval)=await RevocationSeed();var role="openirp_revoke_runtime_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try
        {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grants=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grants.ExecuteNonQueryAsync();
            builder.Username=role;builder.Password=password;await using var runtime=new TestApplication(builder.ConnectionString);using var scope=runtime.Services.CreateScope();
            var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();
            e=await service.RevokeDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id,approval,new(e.Revision,e.IncidentRevision,"Revocar runtime"));
            Assert.False((await service.DependencyReadinessAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id)).AllDependenciesSatisfied);
            Assert.Equal(approval,(await service.DependencyRevocationAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id,approval)).ApprovalId);
            var db=scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
            foreach(var sql in new[]{"UPDATE app.dependency_exception_revocations SET reason='Alterado'","DELETE FROM app.dependency_exception_revocations"})Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }
}
