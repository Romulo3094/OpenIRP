using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private Task<TaskDependencyReadiness> DependencyReadiness(ExecutionFixture f, ExecutionItem e, Guid? task = null, Guid? actor = null) =>
        Executions(s => s.DependencyReadinessAsync(actor ?? f.Admin, f.Org, f.Incident.Id, e.Id, task ?? e.Tasks[1].Id));

    private async Task AssertDependencyReadinessMatchesSql(ExecutionFixture f, ExecutionItem e, TaskDependencyReadiness report)
    {
        var sql = await InScope((_, db) => db.Database.SqlQuery<bool>($"""SELECT app.execution_dependencies_satisfied({e.Id},{report.TaskId},{report.AssignedMembershipId}) AS "Value" """).SingleAsync());
        Assert.Equal(sql, report.AllDependenciesSatisfied);
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task IndependentTasksHaveNoDependenciesWithoutGrantingTransitionPermissionOrWriting()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        var before = await InScope((_, db) => db.AuditEvents.CountAsync());
        var report = await DependencyReadiness(f, e, e.Tasks[0].Id);
        Assert.Empty(report.Dependencies); Assert.Equal(0, report.DependencyCount); Assert.Equal(0, report.UnsatisfiedCount);
        Assert.True(report.AllDependenciesSatisfied); Assert.True(report.RequiresHumanReview); Assert.Null(report.AssignedMembershipId);
        Assert.Equal(e.Revision, report.ExecutionRevision); Assert.Equal(e.IncidentRevision, report.IncidentRevision);
        await AssertDependencyReadinessMatchesSql(f, e, report);
        Assert.Equal(before, await InScope((_, db) => db.AuditEvents.CountAsync()));
        Assert.Equal(e.Revision, (await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id))).Revision);
        await Rejected(409, () => TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.InProgress));
    }

    [Theory, Trait("Feature", "DependencyReadiness")]
    [InlineData("PENDING")] [InlineData("IN_PROGRESS")] [InlineData("COMPLETED")]
    public async Task ReadinessReportsActualDirectPredecessorState(string status)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id);
        if (status != "PENDING") e = await TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.InProgress);
        if (status == "COMPLETED") e = await TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.Completed);
        var report = await DependencyReadiness(f, e); var edge = Assert.Single(report.Dependencies);
        Assert.Equal(e.Tasks[0].Id, edge.PredecessorTaskId); Assert.Equal(status, edge.PredecessorStatus);
        Assert.Equal(status == "COMPLETED" ? ExecutionDependencySatisfaction.PredecessorCompleted : ExecutionDependencySatisfaction.Unsatisfied, edge.Satisfaction);
        Assert.Null(edge.LatestRequestId); Assert.Null(edge.LatestDecisionId);
        await AssertDependencyReadinessMatchesSql(f, e, report);
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task ReadinessSeparatesRequestDecisionAndCurrentSatisfaction()
    {
        var (f, e) = await DependencySeed(controls: true);
        var missing = Assert.Single((await DependencyReadiness(f, e)).Dependencies);
        Assert.Null(missing.LatestRequestId); Assert.Equal(ExecutionDependencySatisfaction.Unsatisfied, missing.Satisfaction);
        e = await AskDependency(f, e); var request = await LastDependencyRequest(f, e);
        var requested = Assert.Single((await DependencyReadiness(f, e)).Dependencies);
        Assert.Equal(request, requested.LatestRequestId); Assert.Equal(e.Revision, requested.LatestRequestRevision); Assert.Null(requested.LatestDecisionAction);
        e = await DecideDependency(f, e, request, approve: false, self: false, justification: null);
        var rejected = Assert.Single((await DependencyReadiness(f, e)).Dependencies);
        Assert.Equal("REJECTED", rejected.LatestDecisionAction); Assert.Equal(ExecutionDependencySatisfaction.Unsatisfied, rejected.Satisfaction);
        e = await AskDependency(f, e); var fresh = await LastDependencyRequest(f, e);
        Assert.NotEqual(request, fresh); e = await DecideDependency(f, e, fresh);
        var report = await DependencyReadiness(f, e); var approved = Assert.Single(report.Dependencies);
        Assert.Equal(fresh, approved.LatestRequestId); Assert.Equal("APPROVED", approved.LatestDecisionAction); Assert.NotNull(approved.LatestDecisionId);
        Assert.Equal(ExecutionDependencySatisfaction.ApprovedException, approved.Satisfaction); Assert.Equal("SKIPPED", approved.PredecessorStatus);
        await AssertDependencyReadinessMatchesSql(f, e, report);
        // A satisfied dependency does not replace the independent STEP_APPROVAL guard.
        await Rejected(409, () => TaskState(f, e, e.Tasks[1].Id, ExecutionTaskState.InProgress));
    }

    [Theory, Trait("Feature", "DependencyReadiness")]
    [InlineData("assignment")] [InlineData("profile")] [InlineData("catalog")] [InlineData("assessment")] [InlineData("cancelled")] [InlineData("withdrawn")]
    public async Task HistoricalApprovalDoesNotClaimCurrentValidityAfterInvalidation(string change)
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e); e = await DecideDependency(f, e, await LastDependencyRequest(f, e));
        var capability = f.Playbook.Requirements[0].CapabilityId;
        if (change == "assignment") e = await AssignTask(f, e, e.Tasks[1].Id);
        else if (change == "profile") await Inventory(s => s.PutCapabilityAsync(f.Admin, f.Org, capability, new(CapabilityAvailability.Available, "Nuevo perfil", 0)));
        else if (change == "catalog") await Inventory(s => s.UpdateCatalogAsync(f.Platform, CatalogKind.Capabilities, capability, new("Catalogo revisado", "CUSTOM", null, true, 1)));
        else if (change == "assessment") await Incidents(s => s.AssessCapabilityAsync(f.Admin, f.Org, f.Incident.Id, capability, Assessment(e.IncidentRevision, CapabilityAvailability.Unknown)));
        else if (change == "cancelled") e = await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Cancelar")));
        else await Playbooks(s => s.WithdrawAsync(f.Platform, null, f.Playbook.Playbook.Id, f.Playbook.VersionId, new("Retirada", 3)));
        var report = await DependencyReadiness(f, e); var edge = Assert.Single(report.Dependencies);
        Assert.Equal("APPROVED", edge.LatestDecisionAction); Assert.NotNull(edge.LatestDecisionId);
        Assert.Equal(ExecutionDependencySatisfaction.Unsatisfied, edge.Satisfaction); Assert.False(report.AllDependenciesSatisfied);
        await AssertDependencyReadinessMatchesSql(f, e, report);
    }

    [Theory, Trait("Feature", "DependencyReadiness")]
    [InlineData("platform")] [InlineData("viewer")] [InlineData("analyst")] [InlineData("inactive-account")] [InlineData("lockout")] [InlineData("suspended-member")] [InlineData("suspended-organization")]
    public async Task DependencyReadinessRevalidatesOrganizationMembershipAndAccount(string role)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        if (role == "platform") { await Rejected(403, () => DependencyReadiness(f, e, actor: f.Platform)); return; }
        var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, role == "analyst" ? OrganizationRole.Analyst : OrganizationRole.Viewer)));
        if (role is "inactive-account" or "lockout") await InScope(async (_, db) => { var u = await db.Users.SingleAsync(u => u.Id == f.Platform); if (role == "lockout") u.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); else u.IsActive = false; return await db.SaveChangesAsync(); });
        else if (role == "suspended-member") await Act(s => s.ChangeMemberAsync(f.Admin, f.Org, member.Id, new(OrganizationRole.Viewer, MembershipStatus.Suspended, member.UpdatedAt)));
        else if (role == "suspended-organization") await InScope(async (_, db) => { (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended; return await db.SaveChangesAsync(); });
        if (role is "viewer" or "analyst") Assert.Equal(e.Id, (await DependencyReadiness(f, e, actor: f.Platform)).ExecutionId);
        else await Rejected(403, () => DependencyReadiness(f, e, actor: f.Platform));
    }

    [Theory, Trait("Feature", "DependencyReadiness")]
    [InlineData("organization")] [InlineData("incident")] [InlineData("execution")] [InlineData("task")]
    public async Task DependencyReadinessRejectsUnknownScopeWithoutDisclosingHistory(string missing)
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e);
        await Rejected(missing == "organization" ? 403 : 404, () => Executions(s => s.DependencyReadinessAsync(f.Admin,
            missing == "organization" ? Guid.NewGuid() : f.Org, missing == "incident" ? Guid.NewGuid() : f.Incident.Id,
            missing == "execution" ? Guid.NewGuid() : e.Id, missing == "task" ? Guid.NewGuid() : e.Tasks[1].Id)));
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task DependencyReadinessRequiresSessionAndUsesNoStoreTypedEnumsWithoutCsrfOnGet()
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e); e = await DecideDependency(f, e, await LastDependencyRequest(f, e));
        var route = $"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[1].Id}/dependency-readiness";
        using var anon = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(route)).StatusCode);
        using var admin = Client(); await Login(admin, "alpha"); admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await admin.GetAsync(route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.GetProperty("allDependenciesSatisfied").GetBoolean()); Assert.True(json.GetProperty("requiresHumanReview").GetBoolean());
        Assert.Equal("ApprovedException", json.GetProperty("dependencies")[0].GetProperty("satisfaction").GetString());
    }

    private sealed class DependencySnapshotInterceptor(Func<Task> change) : DbCommandInterceptor
    {
        public int Calls { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("WITH snapshot AS MATERIALIZED", StringComparison.Ordinal)) { Calls++; await change(); }
            return result;
        }
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task ConcurrentContextChangeDoesNotMixSnapshotRevisionsWithNewExceptionValidity()
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e); e = await DecideDependency(f, e, await LastDependencyRequest(f, e));
        var interceptor = new DependencySnapshotInterceptor(async () =>
            await Inventory(s => s.PutCapabilityAsync(f.Admin, f.Org, f.Playbook.Requirements[0].CapabilityId, new(CapabilityAvailability.Available, "Cambio concurrente", 0))));
        var connection = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()!));
        await using var app = new ReadinessApplication(connection, interceptor); using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IExecutionService>().DependencyReadinessAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[1].Id);
        Assert.Equal(1, interceptor.Calls); Assert.True(report.AllDependenciesSatisfied); Assert.Equal(e.Revision, report.ExecutionRevision);
        Assert.False((await DependencyReadiness(f, e)).AllDependenciesSatisfied);
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task EveryIncomingEdgeRequiresItsOwnCurrentException()
    {
        var f = await ExecutionSeed(customize: c => c with { Dependencies = [new(c.Steps[0].Code, c.Steps[2].Code), new(c.Steps[1].Code, c.Steps[2].Code)] });
        var e = await BeginExecution(f);
        foreach (var t in e.Tasks.Take(2).ToArray()) { e = await AssignTask(f, e, t.Id); e = await AskSkip(f, e, task: t.Id); e = await DecideSkip(f, e, await LastSkipRequest(f, e, t.Id), task: t.Id); }
        var target = e.Tasks[2].Id; e = await AssignTask(f, e, target);
        e = await AskDependency(f, e, target: target, predecessor: e.Tasks[0].Id);
        e = await DecideDependency(f, e, await LastDependencyRequest(f, e, target, e.Tasks[0].Id), target: target, predecessor: e.Tasks[0].Id);
        var report = await DependencyReadiness(f, e, target);
        Assert.Equal(2, report.DependencyCount); Assert.Equal(1, report.UnsatisfiedCount); Assert.False(report.AllDependenciesSatisfied);
        Assert.Equal(e.Tasks.Take(2).Select(t => t.Id), report.Dependencies.Select(d => d.PredecessorTaskId));
        await AssertDependencyReadinessMatchesSql(f, e, report);
        e = await AskDependency(f, e, target: target, predecessor: e.Tasks[1].Id);
        e = await DecideDependency(f, e, await LastDependencyRequest(f, e, target, e.Tasks[1].Id), target: target, predecessor: e.Tasks[1].Id);
        report = await DependencyReadiness(f, e, target); Assert.True(report.AllDependenciesSatisfied);
        Assert.All(report.Dependencies, d => Assert.Equal(ExecutionDependencySatisfaction.ApprovedException, d.Satisfaction));
        await AssertDependencyReadinessMatchesSql(f, e, report);
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task ActualForeignOrganizationAndOtherExecutionTasksCannotLeakDependencyHistory()
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e); e = await DecideDependency(f, e, await LastDependencyRequest(f, e));
        var beta = await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var member = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == beta.Id));
        var incident = await RegisterIncident(member.UserId, beta.Id);
        incident = await MoveWorkflow(member.UserId, beta.Id, incident, IncidentState.UnderEvaluation);
        incident = await MoveWorkflow(member.UserId, beta.Id, incident, IncidentState.InResponse);
        var other = new ExecutionFixture(f.Platform, member.UserId, member.Id, beta.Id, incident, f.Playbook); var foreign = await BeginExecution(other);
        await Rejected(403, () => DependencyReadiness(f, e, actor: member.UserId));
        await Rejected(404, () => DependencyReadiness(f, e, foreign.Tasks[1].Id));
        await Rejected(404, () => Executions(s => s.DependencyReadinessAsync(member.UserId, beta.Id, f.Incident.Id, e.Id, e.Tasks[1].Id)));
        var report = await DependencyReadiness(other, foreign); Assert.Null(Assert.Single(report.Dependencies).LatestRequestId);
        var another = await RegisterIncident(f.Admin, f.Org);
        another = await MoveWorkflow(f.Admin, f.Org, another, IncidentState.UnderEvaluation);
        another = await MoveWorkflow(f.Admin, f.Org, another, IncidentState.InResponse);
        var sameOrg = new ExecutionFixture(f.Platform, f.Admin, f.Membership, f.Org, another, f.Playbook); var sibling = await BeginExecution(sameOrg);
        await Rejected(404, () => DependencyReadiness(f, e, sibling.Tasks[1].Id));
    }

    [Fact, Trait("Feature", "DependencyReadiness")]
    public async Task RestrictedRuntimeReadsCurrentDependencySatisfactionWithoutAdditionalGrants()
    {
        var (f, e) = await DependencySeed(); e = await AskDependency(f, e); e = await DecideDependency(f, e, await LastDependencyRequest(f, e));
        var role = "openirp_dependency_read_" + Guid.NewGuid().ToString("N"); var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)", owner)) { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using var runtime = new TestApplication(builder.ConnectionString); using var scope = runtime.Services.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<IExecutionService>().DependencyReadinessAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[1].Id);
            Assert.True(report.AllDependenciesSatisfied); Assert.Equal(ExecutionDependencySatisfaction.ApprovedException, Assert.Single(report.Dependencies).Satisfaction);
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync(); }
    }
}
