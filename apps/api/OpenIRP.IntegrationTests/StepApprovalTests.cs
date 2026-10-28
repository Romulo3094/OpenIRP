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
using Microsoft.Extensions.DependencyInjection;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<ExecutionItem> AskStep(ExecutionFixture f, ExecutionItem e, Guid? actor = null) =>
        Executions(s => s.RequestApprovalAsync(actor ?? f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id,
            new(e.Revision, e.IncidentRevision, "Solicitar autorización del paso")));
    private async Task<Guid> LastStepRequest(ExecutionFixture f, ExecutionItem e) =>
        (await Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id))).Last(a => a.Action == "REQUESTED").Id;
    private Task<ExecutionItem> DecideStep(ExecutionFixture f, ExecutionItem e, Guid request, bool approve = true, Guid? actor = null, bool self = true, string? justification = "Excepción revisada por administrador único") =>
        Executions(s => s.DecideApprovalAsync(actor ?? f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id, request,
            new(approve, self, justification, e.Revision, e.IncidentRevision, "Decisión documentada")));

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task StepApprovalSeparatesRequesterAndAdminAndPreservesAuditContext()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f); var task = e.Tasks[0].Id;
        var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Analyst)));
        e = await AssignTask(f, e, task, member.Id);
        await Rejected(409, () => TaskState(f, e, task, ExecutionTaskState.InProgress, f.Platform), "task_approval_required");
        e = await AskStep(f, e, f.Platform); var id = await LastStepRequest(f, e);
        await Rejected(403, () => DecideStep(f, e, id, actor: f.Platform));
        e = await DecideStep(f, e, id, self: false, justification: null);
        e = await TaskState(f, e, task, ExecutionTaskState.InProgress, f.Platform);
        e = await TaskState(f, e, task, ExecutionTaskState.Completed, f.Platform);
        var history = await Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, task));
        Assert.Equal(new[] { "REQUESTED", "APPROVED" }, history.Select(a => a.Action));
        Assert.Equal(history[0].Id, history[1].RequestId); Assert.False(history[1].SelfApproval);
        var events = await Executions(s => s.EventsAsync(f.Admin, f.Org, f.Incident.Id, e.Id));
        foreach (var row in history)
        {
            var matching = Assert.Single(events, a => a.Revision == row.ExecutionRevision);
            Assert.Equal(row.CorrelationId, matching.CorrelationId); Assert.Equal(row.ActorUserId, matching.ActorUserId);
        }
        Assert.Equal("COMPLETED", e.Tasks[0].Status);
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    public async Task StepSelfApprovalRequiresExplicitJustifiedException(bool self, string? reason)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); e = await AskStep(f, e); var id = await LastStepRequest(f, e);
        await Rejected(400, () => DecideStep(f, e, id, self: self, justification: reason), "invalid_step_self_approval");
        Assert.Single(await Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id)));
        e = await DecideStep(f, e, id);
        e = await TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.InProgress);
        Assert.Equal("IN_PROGRESS", e.Tasks[0].Status);
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReassignmentInvalidatesPendingAndApprovedRequestsEvenForSameAssignee(bool approveFirst)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        var task = e.Tasks[0].Id; e = await AssignTask(f, e, task); e = await AskStep(f, e); var old = await LastStepRequest(f, e);
        if (approveFirst) e = await DecideStep(f, e, old);
        e = await AssignTask(f, e, task);
        await Rejected(409, () => DecideStep(f, e, old), "step_approval_request_obsolete");
        await Rejected(409, () => TaskState(f, e, task, ExecutionTaskState.InProgress), "task_approval_required");
        e = await AskStep(f, e); var fresh = await LastStepRequest(f, e); Assert.NotEqual(old, fresh);
        e = await DecideStep(f, e, fresh); e = await TaskState(f, e, task, ExecutionTaskState.InProgress);
        await Rejected(409, () => AssignTask(f, e, task), "block_task_before_reassignment");
        e = await TaskState(f, e, task, ExecutionTaskState.Blocked);
        e = await AssignTask(f, e, task);
        await Rejected(409, () => TaskState(f, e, task, ExecutionTaskState.InProgress), "task_approval_required");
    }

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task RejectionAllowsNewRequestButDecisionsCannotBeOverwritten()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); e = await AskStep(f, e); var old = await LastStepRequest(f, e);
        await Rejected(409, () => AskStep(f, e));
        e = await DecideStep(f, e, old, approve: false, self: false, justification: null);
        await Rejected(409, () => DecideStep(f, e, old), "step_approval_already_decided");
        e = await AskStep(f, e); var fresh = await LastStepRequest(f, e);
        await Rejected(409, () => DecideStep(f, e, old), "step_approval_request_obsolete");
        e = await DecideStep(f, e, fresh);
        await Rejected(409, () => AskStep(f, e));
        Assert.Equal(4, (await Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id))).Count);
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData("organization")]
    [InlineData("assignee")]
    [InlineData("account")]
    [InlineData("lockout")]
    [InlineData("withdrawn")]
    [InlineData("cancelled")]
    public async Task ApprovalDecisionRechecksCurrentParticipantsAndExecution(string invalid)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Analyst)));
        e = await AssignTask(f, e, e.Tasks[0].Id, member.Id); e = await AskStep(f, e, f.Platform); var id = await LastStepRequest(f, e);
        if (invalid == "organization") await InScope(async (_, db) => { (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended; return await db.SaveChangesAsync(); });
        else if (invalid == "assignee") await Act(s => s.ChangeMemberAsync(f.Admin, f.Org, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Suspended, member.UpdatedAt)));
        else if (invalid is "account" or "lockout")
            await InScope(async (_, db) => { var user = await db.Users.SingleAsync(u => u.Id == f.Platform); if (invalid == "account") user.IsActive = false; else user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); return await db.SaveChangesAsync(); });
        else if (invalid == "withdrawn") await Playbooks(s => s.WithdrawAsync(f.Platform, null, f.Playbook.Playbook.Id, f.Playbook.VersionId, new("Retirada durante revisión", 3)));
        else e = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Cancelada")));
        await Rejected(invalid == "organization" ? 403 : 409, () => DecideStep(f, e, id, self: false, justification: null));
        Assert.Equal(1, await InScope((_, db) => db.StepApprovals.CountAsync()));
    }

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task ApprovalScopeRolesPaginationAndStaleTokensAreEnforced()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f); var task = e.Tasks[0].Id;
        await Rejected(403, () => Executions(s => s.ApprovalsAsync(f.Platform, f.Org, f.Incident.Id, e.Id, task)));
        var viewer = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Viewer)));
        Assert.Empty(await Executions(s => s.ApprovalsAsync(f.Platform, f.Org, f.Incident.Id, e.Id, task)));
        await Rejected(403, () => AskStep(f, e, f.Platform));
        await Rejected(404, () => Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, Guid.NewGuid())));
        await Rejected(400, () => Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, task, -1)));
        await Rejected(400, () => Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, task, 0, 101)));
        e = await AssignTask(f, e, task); var stale = e; e = await AskStep(f, e);
        await Rejected(409, () => AskStep(f, stale), "stale_incident_revision");
        await Rejected(409, () => Executions(s => s.RequestApprovalAsync(f.Admin, f.Org, f.Incident.Id, e.Id, task, new(stale.Revision, e.IncidentRevision, "Token obsoleto"))), "stale_execution_revision");
        Assert.Single(await Executions(s => s.ApprovalsAsync(f.Platform, f.Org, f.Incident.Id, e.Id, task, 0, 1)));
        Assert.Empty(await Executions(s => s.ApprovalsAsync(f.Platform, f.Org, f.Incident.Id, e.Id, task, e.Revision)));
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData("request", "audit")]
    [InlineData("decision", "audit")]
    [InlineData("decision", "events")]
    [InlineData("request", "approvals")]
    [InlineData("decision", "approvals")]
    public async Task ApprovalFailuresRollBackDecisionAndBothAggregateRevisions(string operation, string sink)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id);
        Guid request = Guid.Empty;
        if (operation == "decision") { e = await AskStep(f, e); request = await LastStepRequest(f, e); }
        var count = await InScope((_, db) => db.StepApprovals.CountAsync());
        var table = sink switch { "audit" => "audit.audit_events", "approvals" => "app.step_approvals", _ => "app.execution_task_events" };
        var script = "CREATE FUNCTION app.fail_approval_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $body$; CREATE TRIGGER fail_approval_test BEFORE INSERT ON " + table + " FOR EACH ROW " + (sink == "audit" ? "WHEN (NEW.outcome='SUCCESS') " : "") + "EXECUTE FUNCTION app.fail_approval_test();";
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(script));
        await Assert.ThrowsAsync<DbUpdateException>(async () => { if (operation == "request") await AskStep(f, e); else await DecideStep(f, e, request); });
        var after = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id));
        Assert.Equal(e.Revision, after.Revision); Assert.Equal(e.IncidentRevision, after.IncidentRevision);
        Assert.Equal(count, await InScope((_, db) => db.StepApprovals.CountAsync()));
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task DatabasePreservesStepApprovalHistory(string operation)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); await AskStep(f, e);
        var sql = operation switch { "update" => "UPDATE app.step_approvals SET reason='Manipulado'", "delete" => "DELETE FROM app.step_approvals", _ => "TRUNCATE app.step_approvals" };
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("42501", ex.SqlState);
        await InScope(async (_, db) => { var row = await db.StepApprovals.SingleAsync(); row.Reason = "Manipulado EF"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); return true; });
    }

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task DatabaseRejectsForgedApprovalWithoutMatchingHistoryAndUnapprovedTaskStart()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f); var task = e.Tasks[0].Id;
        e = await AssignTask(f, e, task);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.StepApprovals.Add(new() { OrganizationId=f.Org,IncidentId=f.Incident.Id,ExecutionId=e.Id,VersionId=e.VersionId,TaskId=task,AssignedMembershipId=f.Membership,ActorUserId=f.Admin,ExecutionRevision=e.Revision+1,Reason="Sin evento" });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlAsync($"UPDATE app.execution_tasks SET status='IN_PROGRESS',changed_at_revision=changed_at_revision+1 WHERE id={task}")));
        Assert.Equal("23514", blocked.SqlState);
    }

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task ApprovalMigrationUpgradesExistingExecutionAndPreventsHistoryLoss()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261024034804_InitialPlaybookExecutions"); return true; });
        await InScope(async (_, db) => { await db.Database.MigrateAsync(); return true; });
        e = await AssignTask(f, e, e.Tasks[0].Id); e = await AskStep(f, e);
        await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261024034804_InitialPlaybookExecutions"); return true; }));
        await InScope(async (_, db) => { await db.Database.MigrateAsync(); return true; });
        Assert.Single(await Executions(s => s.ApprovalsAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id)));
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentApprovalRequestsAndDecisionsCannotOverwriteEachOther(bool decision)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); Guid id = Guid.Empty;
        if (decision) { e = await AskStep(f, e); id = await LastStepRequest(f, e); }
        async Task<int> Attempt()
        {
            try { if (decision) await DecideStep(f, e, id); else await AskStep(f, e); return 200; }
            catch (IdentityOperationException ex) { return ex.StatusCode; }
        }
        Assert.Equal(new[] {200,409}, (await Race(Attempt, Attempt)).Order().ToArray());
        Assert.Equal(decision ? 2 : 1, await InScope((_, db) => db.StepApprovals.CountAsync()));
    }

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task HttpStepApprovalEnforcesAuthenticationCsrfAndRequiredDecisionFlags()
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f); e = await AssignTask(f, e, e.Tasks[0].Id);
        var route = $"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[0].Id}/approvals";
        using var anon = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(route)).StatusCode);
        using var admin = Client(); await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(route, new ExecutionCommand(e.Revision,e.IncidentRevision,"Revisión HTTP"))).StatusCode);
        e = await Executions(s => s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id)); var id = await LastStepRequest(f,e);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route+$"/{id}/decision",new {expectedExecutionRevision=e.Revision,expectedIncidentRevision=e.IncidentRevision,reason="Faltan flags"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(route+$"/{id}/decision",new DecideStepApproval(true,true,"Excepción explícita",e.Revision,e.IncidentRevision,"Autorizar HTTP"))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route,new ExecutionCommand(5,8,"Sin CSRF"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(route)).StatusCode);
    }
    [Fact, Trait("Feature", "StepApprovals")]
    public async Task RestrictedRuntimeCanRequestAndApproveStepsWithoutRewritingDecisions()
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
                current = await service.RequestApprovalAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task,new(current.Revision,current.IncidentRevision,"Runtime request"));
                var request = (await service.ApprovalsAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task)).Single().Id;
                current = await service.DecideApprovalAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task,request,new(true,true,"Excepción runtime explícita",current.Revision,current.IncidentRevision,"Runtime approve"));
                current = await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,current.Id,task,new(ExecutionTaskState.InProgress,current.Revision,current.IncidentRevision,"Runtime start task"));
                current = await service.ChangeAsync(f.Admin, f.Org, f.Incident.Id, current.Id, task, new(ExecutionTaskState.Completed, current.Revision, current.IncidentRevision, "Runtime finish task"));
                var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach (var sql in new[] { "UPDATE app.execution_tasks SET step_id=gen_random_uuid()", "DELETE FROM app.execution_task_events", "DELETE FROM app.playbook_executions", "UPDATE app.step_approvals SET reason='Alterado'", "DELETE FROM app.step_approvals" })
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

    [Fact, Trait("Feature", "StepApprovals")]
    public async Task ApprovalDoesNotReplaceEvidenceAndSurvivesBlockingWithoutReassignment()
    {
        var f = await ExecutionSeed(approvals: true, approvalEvidence: true); var e = await BeginExecution(f); var task = e.Tasks[0].Id;
        e = await AssignTask(f,e,task); e = await AskStep(f,e); e = await DecideStep(f,e,await LastStepRequest(f,e));
        e = await TaskState(f,e,task,ExecutionTaskState.InProgress);
        e = await TaskState(f,e,task,ExecutionTaskState.Blocked);
        e = await TaskState(f,e,task,ExecutionTaskState.InProgress);
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.Completed),"task_evidence_required");
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.Skipped),"task_skip_authorization_not_available");
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestChecksRequiredFlagAndCompletedDependencies(bool dependencies)
    {
        var f = await ExecutionSeed(controls: dependencies); var e = await BeginExecution(f);
        var task = e.Tasks[dependencies ? 1 : 0].Id; e = await AssignTask(f,e,task);
        await Rejected(409,()=>Executions(s=>s.RequestApprovalAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,
            new(e.Revision,e.IncidentRevision,"Revisión inválida"))),dependencies ? "execution_dependencies_not_completed" : "step_approval_not_required");
    }

    [Theory, Trait("Feature", "StepApprovals")]
    [InlineData("role")]
    [InlineData("self")]
    [InlineData("history")]
    public async Task DatabaseRejectsUnauthorizedOrUnloggedDecisions(string invalid)
    {
        var f = await ExecutionSeed(approvals: true); var e = await BeginExecution(f); var member = f.Membership; var requester = f.Admin;
        if(invalid=="role") { var analyst = await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst))); member=analyst.Id; requester=f.Platform; }
        e=await AssignTask(f,e,e.Tasks[0].Id,member); e=await AskStep(f,e,requester); var id=await LastStepRequest(f,e);
        var ex=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async(_,db)=>
        {
            db.StepApprovals.Add(new() { OrganizationId=f.Org,IncidentId=f.Incident.Id,VersionId=e.VersionId,ExecutionId=e.Id,
                TaskId=e.Tasks[0].Id,AssignedMembershipId=member,RequestId=id,Action="APPROVED",ExecutionRevision=e.Revision+1,
                ActorUserId=requester,Reason="Intento SQL",SelfApproval=invalid!="self",SelfApprovalJustification=invalid!="self" ? "Excepción explícita" : null });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        Assert.Equal(1,await InScope((_,db)=>db.StepApprovals.CountAsync()));
    }
}
