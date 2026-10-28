using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Npgsql;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<IncidentClosureReadiness> ClosureRead(Guid actor, Guid org, Guid incident) =>
        Incidents(s => s.ClosureReadinessAsync(actor, org, incident));

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task ClosureChecklistPreservesUnknownInputsAndDoesNotWrite()
    {
        var f = await Seed(); var i = await RegisterIncident(f.Admin, f.Org.Id);
        var before = await InScope((_, db) => db.AuditEvents.CountAsync());
        var r = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.Equal(i.Revision, r.IncidentRevision); Assert.False(r.ClassificationComplete);
        Assert.False(r.HasResolution); Assert.False(r.AssignedWriterActive);
        Assert.Equal(new[] { "current_closure_risk_acceptance_required", "incident_not_resolved", "incident_resolution_required", "incident_classification_pending", "active_assigned_incident_writer_required" }, r.Blockers);
        Assert.False(r.CanCloseDirectly); Assert.False(r.CanRequestClosure); Assert.False(r.CanApproveClosure);
        Assert.True(r.RequiresHumanReview); Assert.False(r.ResidualRiskEvaluated); Assert.False(r.FileIntegrityVerified);
        Assert.Empty(r.Executions); Assert.Empty(r.Tasks); Assert.Empty(r.Evidence); Assert.Null(r.ActiveExecutionId);
        Assert.Equal(before, await InScope((_, db) => db.AuditEvents.CountAsync()));
        Assert.Equal(i.Revision, (await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id))).Revision);
    }

    [Theory, Trait("Feature", "ClosureReadiness")]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosureChecklistPredictsExistingLowAndHighClosurePaths(bool high)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, high);
        var r = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.Empty(r.Blockers); Assert.True(r.ClassificationComplete); Assert.True(r.HasResolution);
        Assert.Equal(high, r.AdministrativeApprovalRequired); Assert.Equal(!high, r.CanCloseDirectly);
        Assert.Equal(high, r.CanRequestClosure); Assert.False(r.CanApproveClosure);
        if (!high) Assert.Equal("CLOSED", (await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.Closed)).Status);
        else Assert.Equal(ClosureReviewStatus.Pending, (await AskClosure(f.Admin, f.Org.Id, i)).ClosureReview!.Status);
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task HistoricalMaximumStillRequiresReviewAfterSeverityReduction()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership);
        i = await Incidents(s => s.AssessImpactAsync(f.Admin, f.Org.Id, i.Id, new(ImpactLevel.None, ImpactLevel.None, ImpactLevel.None, "Reevaluacion", i.Revision)));
        Assert.Equal(IncidentSeverity.Low, i.Severity);
        var r = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.True(r.AdministrativeApprovalRequired); Assert.False(r.CanRequestClosure); Assert.False(r.CanCloseDirectly);
        Assert.False(r.RiskReview.AcceptanceCurrent);
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task PendingReviewShowsActorSpecificApprovalAndSelfApprovalDeclaration()
    {
        var f = await Seed(); await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, OrganizationRole.OrgAdmin)));
        var i = await AskClosure(f.Admin, f.Org.Id, await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership));
        var own = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        var other = await ClosureRead(f.Platform, f.Org.Id, i.Id);
        Assert.True(own.ClosureRequestCurrent); Assert.False(own.CanRequestClosure); Assert.True(own.CanApproveClosure);
        Assert.True(own.SelfApprovalDeclarationRequired); Assert.True(other.CanApproveClosure); Assert.False(other.SelfApprovalDeclarationRequired);
        Assert.Equal(i.ClosureReview, own.ClosureReview);
        var closed = await ReviewClosure(f.Platform, f.Org.Id, i);
        var terminal = await ClosureRead(f.Admin, f.Org.Id, closed.Id);
        Assert.Contains("incident_not_resolved", terminal.Blockers); Assert.False(terminal.ClosureRequestCurrent);
        Assert.False(terminal.CanApproveClosure); Assert.False(terminal.CanCloseDirectly);
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task InvalidatedReviewIsReportedAsHistoryAndCanBeRequestedAgain()
    {
        var f = await Seed(); var i = await AskClosure(f.Admin, f.Org.Id, await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership));
        i = await Incidents(s => s.UpdateAsync(f.Admin, f.Org.Id, i.Id, new(i.CategoryId, "Correction", null, "Motivo", i.Revision)));
        var r = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.Equal(ClosureReviewStatus.Invalidated, r.ClosureReview!.Status); Assert.False(r.ClosureRequestCurrent);
        Assert.False(r.CanApproveClosure); Assert.False(r.CanRequestClosure);
        i = await AcceptRiskForTest(f.Admin, f.Org.Id, i);
        Assert.True((await ClosureRead(f.Admin, f.Org.Id, i.Id)).CanRequestClosure);
    }

    [Theory, Trait("Feature", "ClosureReadiness")]
    [InlineData("user")] [InlineData("lockout")] [InlineData("membership")]
    public async Task AssignedWriterEligibilityIsCurrentEvenWhenReaderIsAnotherActiveAdmin(string change)
    {
        var f = await Seed(); await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, OrganizationRole.OrgAdmin)));
        var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        await InScope(async (_, db) => {
            if (change == "membership") (await db.OrganizationMemberships.SingleAsync(m => m.Id == f.Membership)).Status = MembershipStatus.Suspended;
            else { var user = await db.Users.SingleAsync(u => u.Id == f.Admin); if (change == "user") user.IsActive = false; else user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); }
            return await db.SaveChangesAsync();
        });
        var r = await ClosureRead(f.Platform, f.Org.Id, i.Id);
        Assert.False(r.AssignedWriterActive); Assert.Contains("active_assigned_incident_writer_required", r.Blockers);
        Assert.False(r.CanCloseDirectly); Assert.False(r.CanApproveClosure);
    }

    [Theory, Trait("Feature", "ClosureReadiness")]
    [InlineData(OrganizationRole.Viewer)] [InlineData(OrganizationRole.Analyst)] [InlineData(OrganizationRole.OrgAdmin)]
    public async Task ReadPermissionDoesNotGrantUnassignedActorClosureRights(OrganizationRole role)
    {
        var f = await Seed(); await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, role)));
        var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var r = await ClosureRead(f.Platform, f.Org.Id, i.Id);
        Assert.Empty(r.Blockers); Assert.False(r.CanCloseDirectly); Assert.False(r.CanRequestClosure); Assert.False(r.CanApproveClosure);
    }

    [Theory, Trait("Feature", "ClosureReadiness")]
    [InlineData("platform")] [InlineData("suspended")] [InlineData("locked")] [InlineData("organization")]
    public async Task ClosureReadRevalidatesReaderWithoutGlobalPrivilegeBypass(string state)
    {
        var f = await Seed(); var i = await RegisterIncident(f.Admin, f.Org.Id);
        if (state != "platform") await InScope(async (_, db) => {
            if (state == "suspended") (await db.OrganizationMemberships.SingleAsync(m => m.Id == f.Membership)).Status = MembershipStatus.Suspended;
            if (state == "locked") (await db.Users.SingleAsync(u => u.Id == f.Admin)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (state == "organization") (await db.Organizations.SingleAsync(o => o.Id == f.Org.Id)).Status = OrganizationStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => ClosureRead(state == "platform" ? f.Platform : f.Admin, f.Org.Id, i.Id));
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task ClosureReadScopesIncidentToOrganizationAndDoesNotLeakOtherExecutions()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        var other = await RegisterIncident(f.Admin, f.Org);
        var report = await ClosureRead(f.Admin, f.Org, other.Id);
        Assert.Empty(report.Executions); Assert.Empty(report.Tasks); Assert.Null(report.ActiveExecutionId);
        var beta = await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var actor = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        await Rejected(404, () => ClosureRead(actor, beta.Id, f.Incident.Id));
        await Rejected(404, () => ClosureRead(f.Admin, f.Org, Guid.NewGuid()));
        await Rejected(403, () => ClosureRead(f.Admin, beta.Id, other.Id));
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task ActiveExecutionAndCurrentSecurityHoldRemainVisibleAfterContinuity()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); var hold = await HoldVersion(f);
        var r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(e.Id, r.ActiveExecutionId); Assert.Equal(e.Revision, r.ActiveExecutionRevision);
        Assert.Equal(hold.Id, r.ActiveSecurityHoldId); Assert.False(r.ContinuityAuthorized);
        Assert.Contains("incident_has_active_execution", r.Blockers);
        Assert.Equal(3, r.Tasks.Single(t => t.Status == "PENDING").Count);
        e = await DecideHold(f, e, hold.Id);
        r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.True(r.ContinuityAuthorized); Assert.Contains("incident_has_active_execution", r.Blockers);
        var newer = await HoldVersion(f, 4);
        r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(newer.Id, r.ActiveSecurityHoldId); Assert.False(r.ContinuityAuthorized);
        e = await DecideHold(f, e, newer.Id, false);
        r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Null(r.ActiveExecutionId); Assert.Null(r.ActiveSecurityHoldId); Assert.False(r.ContinuityAuthorized);
        Assert.Equal(1, r.Executions.Single(t => t.Status == "CANCELLED").Count);
        Assert.Equal(3, r.Tasks.Single(t => t.Status == "PENDING").Count);
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task ClosureReadHttpIsAuthenticatedTypedAndNoStoreWithoutCsrfOnGet()
    {
        var f = await Seed(); var i = await RegisterIncident(f.Admin, f.Org.Id);
        var path = $"/api/organizations/{f.Org.Id}/incidents/{i.Id}/closure-readiness";
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        using var client = Client(); await Login(client, "alpha"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(i.Id, body.GetProperty("incidentId").GetGuid()); Assert.True(body.GetProperty("requiresHumanReview").GetBoolean());
        Assert.False(body.GetProperty("residualRiskEvaluated").GetBoolean());
    }


    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task HistoricalExceptionCountsNeverClaimCurrentAuthorization()
    {
        var (f, e, approval) = await RevocationSeed();
        var r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(1, r.HistoricalDependencyApprovals); Assert.Equal(0, r.DependencyRevocations);
        e = await RevokeDependency(f, e, approval);
        r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(1, r.HistoricalDependencyApprovals); Assert.Equal(1, r.DependencyRevocations);
        Assert.Equal(1, r.Tasks.Single(t => t.Status == "SKIPPED").Count); Assert.False(r.ResidualRiskEvaluated);
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task EvidenceCountsDoNotExposeContentOrVerifyPhysicalFiles()
    {
        var f = await ExecutionSeed(); var row = await AddEvidence(f);
        await WithFiles(async (runtime, root) => {
            var uploaded = await UploadFile(runtime, f);
            var r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
            Assert.Equal(2, r.Evidence.Single(e => e.Status == "ACTIVE").Count); Assert.Equal(0, r.ActiveUnavailableFiles);
            Assert.False(r.FileIntegrityVerified);
            var withdrawn = await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, f.Incident.Id, row.Evidence.Id, new(1, "Retiro")));
            r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
            Assert.Equal(1, r.Evidence.Single(e => e.Status == "ACTIVE").Count); Assert.Equal(1, r.Evidence.Single(e => e.Status == "WITHDRAWN").Count);
            Assert.DoesNotContain(row.Evidence.Content!, JsonSerializer.Serialize(r));
        });
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task FailedFilePublicationIsCountedAsUnavailableWithoutRetryOrPhysicalWrite()
    {
        var f = await ExecutionSeed();
        await WithFiles(async (runtime, root) => {
            var uploaded = await UploadFile(runtime, f);
            var r = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
            Assert.Equal(1, r.ActiveUnavailableFiles); Assert.False(r.FileIntegrityVerified);
            Assert.Equal(1, r.Evidence.Single(e => e.Status == "ACTIVE").Count);
            var again = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
            Assert.Equal(1, again.ActiveUnavailableFiles);
        }, failure: "publish_once");
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task RestrictedSqlRuntimeCanReadClosureReportWithoutAdditionalGrants()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); var hold = await HoldVersion(f);
        var role = "openirp_closure_" + Guid.NewGuid().ToString("N");
        var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)", owner)) { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using var runtime = new TestApplication(builder.ConnectionString);
            using var scope = runtime.Services.CreateScope();
            var r = await scope.ServiceProvider.GetRequiredService<IIncidentService>().ClosureReadinessAsync(f.Admin, f.Org, f.Incident.Id);
            Assert.Equal(e.Id, r.ActiveExecutionId); Assert.Equal(hold.Id, r.ActiveSecurityHoldId); Assert.False(r.ContinuityAuthorized);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class ClosureConcurrentChange(Func<Task> change) : DbCommandInterceptor
    {
        private int changed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("GROUP BY", StringComparison.Ordinal) && Interlocked.Exchange(ref changed, 1) == 0) await change();
            return result;
        }
    }

    [Fact, Trait("Feature", "ClosureReadiness")]
    public async Task ConcurrentCancellationCannotMixIncidentAndExecutionRevisionsInClosureReport()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); var calls = 0;
        var interceptor = new ClosureConcurrentChange(async () => {
            calls++; await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Cancelacion concurrente")));
        });
        var connection = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()!));
        await using var isolated = new ReadinessApplication(connection, interceptor);
        using var scope = isolated.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<IIncidentService>().ClosureReadinessAsync(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(1, calls); Assert.Equal(e.IncidentRevision, r.IncidentRevision); Assert.Equal(e.Revision, r.ActiveExecutionRevision);
        Assert.Equal(1, r.Executions.Single(x => x.Status == "IN_PROGRESS").Count);
        var latest = await ClosureRead(f.Admin, f.Org, f.Incident.Id);
        Assert.Equal(e.IncidentRevision + 1, latest.IncidentRevision); Assert.Null(latest.ActiveExecutionId);
        Assert.Equal(1, latest.Executions.Single(x => x.Status == "CANCELLED").Count);
    }
}
