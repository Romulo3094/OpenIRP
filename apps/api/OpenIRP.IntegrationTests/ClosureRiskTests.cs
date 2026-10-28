using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<ClosureRiskReviewItem> RiskForTest(Guid actor, Guid org, IncidentItem current, bool accept = true)
    {
        var report = await ClosureRead(actor, org, current.Id);
        var self = await InScope((_, db) => db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == org && m.Id == current.AssignedMembershipId && m.UserId == actor));
        return await Incidents(s => s.ReviewClosureRiskAsync(actor, org, current.Id,
            new(accept, "Evaluacion humana de riesgos, excepciones y mitigaciones restantes", "Revision explicita de cierre",
                self, self ? "Excepcion documentada de revision propia" : null, current.Revision, report.ClosureContextToken)));
    }

    private async Task<IncidentItem> AcceptRiskForTest(Guid preferred, Guid org, IncidentItem current)
    {
        var reviewer = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == org && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin
            && db.Users.Any(u => u.Id == m.UserId && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)))
            .OrderByDescending(m => m.UserId == preferred).ThenBy(m => m.Id).Select(m => m.UserId).FirstAsync());
        await RiskForTest(reviewer, org, current);
        return await Incidents(s => s.GetAsync(reviewer, org, current.Id));
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData(false)] [InlineData(true)]
    public async Task EveryClosureRequiresCurrentAcceptedAdminRiskReview(bool high)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, high, reviewed: false);
        if (high) await Rejected(409, () => Incidents(s => s.RequestClosureAsync(f.Admin, f.Org.Id, i.Id, new("Solicitar", i.Revision))), "current_closure_risk_acceptance_required");
        else await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, i, null), "current_closure_risk_acceptance_required");
        var review = await RiskForTest(f.Admin, f.Org.Id, i);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id));
        var state = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.True(state.ResidualRiskEvaluated); Assert.True(state.RiskReview.AcceptanceCurrent); Assert.Equal(review.Id, state.RiskReview.LatestReview!.Id);
        if (high)
        {
            var context = state.RiskReview.ContextToken;
            i = await AskClosure(f.Admin, f.Org.Id, i);
            var pending = await ClosureRead(f.Admin, f.Org.Id, i.Id);
            Assert.True(pending.RiskReview.AcceptanceCurrent); Assert.Equal(context, pending.RiskReview.ContextToken);
            Assert.Equal(review.Id, pending.RiskReview.LatestReview!.Id);
            i = await ReviewClosure(f.Admin, f.Org.Id, i, true, true, "Excepcion de aprobacion separada de la evaluacion");
        }
        else i = await CloseWithContext(f.Admin, f.Org.Id, i, state.ClosureContextToken);
        Assert.Equal("CLOSED", i.Status);
        var last = (await Incidents(s => s.EventsAsync(f.Admin, f.Org.Id, i.Id))).Items.Last();
        Assert.Equal(review.Id, last.Details.GetProperty("riskReviewId").GetGuid());
        Assert.Single(await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id)));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task NewRejectionSupersedesAcceptedReviewAndNewAcceptanceDoesNotEraseHistory()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var first = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).RiskReview.LatestReview!;
        var rejected = await RiskForTest(f.Admin, f.Org.Id, i, false);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id));
        var r = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        Assert.True(r.ResidualRiskEvaluated); Assert.False(r.RiskReview.AcceptanceCurrent); Assert.False(r.CanCloseDirectly);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, i, null), "current_closure_risk_acceptance_required");
        var accepted = await RiskForTest(f.Admin, f.Org.Id, i); Assert.NotEqual(first.Id, accepted.Id);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id));
        await CloseWithContext(f.Admin, f.Org.Id, i, null);
        var history = await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id));
        Assert.Equal(new[] { first.Id, rejected.Id, accepted.Id }, history.Select(h => h.Id));
        var page = await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id, first.IncidentRevision, 1)); Assert.Equal(rejected.Id, Assert.Single(page).Id);
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData("withdrawal")] [InlineData("profile")] [InlineData("membership")] [InlineData("hold")] [InlineData("category")] [InlineData("incident")]
    public async Task RelevantChangesInvalidateRiskAcceptanceWithoutRequiringClientContextToken(string change)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Respuesta manual")));
        var evidence = await AddEvidence(f);
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        var previous = (await ClosureRead(f.Admin, f.Org, i.Id)).RiskReview.LatestReview!;
        if (change == "withdrawal") await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, i.Id, evidence.Evidence.Id, new(1, "Retiro")));
        if (change == "profile") await Inventory(s => s.PutCapabilityAsync(f.Admin, f.Org, f.Playbook.Requirements[0].CapabilityId, new(CapabilityAvailability.Unavailable, "Limitacion nueva", 0)));
        if (change == "membership") await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Viewer)));
        if (change == "hold") await HoldVersion(f);
        if (change == "category") await Inventory(s => s.UpdateCatalogAsync(f.Platform, CatalogKind.IncidentCategories, i.CategoryId, new("Actualizada", "CUSTOM", null, true, 1)));
        if (change == "incident") i = await Incidents(s => s.UpdateAsync(f.Admin, f.Org, i.Id, new(i.CategoryId, "Correccion", null, "Motivo", i.Revision)));
        var r = await ClosureRead(f.Admin, f.Org, i.Id);
        Assert.False(r.RiskReview.IsCurrent); Assert.Equal(previous.Id, r.RiskReview.LatestReview!.Id); Assert.False(r.CanCloseDirectly);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org, i, null), "current_closure_risk_acceptance_required");
        i = await AcceptRiskForTest(f.Admin, f.Org, i);
        Assert.Equal("CLOSED", (await CloseWithContext(f.Admin, f.Org, i, null)).Status);
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task NewReviewInvalidatesPendingClosureRequestButRequestItselfDoesNotInvalidateRisk()
    {
        var f = await Seed(); var i = await AskClosure(f.Admin, f.Org.Id, await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership));
        var original = i.ClosureReview!.Id;
        await RiskForTest(f.Admin, f.Org.Id, i);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id));
        Assert.Equal(ClosureReviewStatus.Invalidated, i.ClosureReview!.Status);
        Assert.True((await ClosureRead(f.Admin, f.Org.Id, i.Id)).RiskReview.AcceptanceCurrent);
        await Rejected(409, () => ReviewClosure(f.Admin, f.Org.Id, i, true, true, "Propia"));
        var newer = await AskClosure(f.Admin, f.Org.Id, i); Assert.NotEqual(original, newer.ClosureReview!.Id);
        Assert.True((await ClosureRead(f.Admin, f.Org.Id, i.Id)).RiskReview.AcceptanceCurrent);
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData("platform")] [InlineData("analyst")] [InlineData("viewer")] [InlineData("suspended")] [InlineData("locked")] [InlineData("inactive")]
    public async Task RiskReviewerMustBeCurrentLocalOrganizationAdmin(string condition)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        if (condition != "platform")
        {
            var role = condition == "analyst" ? OrganizationRole.Analyst : condition == "viewer" ? OrganizationRole.Viewer : OrganizationRole.OrgAdmin;
            var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, role)));
            if (condition == "suspended") await Act(s => s.ChangeMemberAsync(f.Admin, f.Org.Id, member.Id, new(role, MembershipStatus.Suspended, member.UpdatedAt)));
            if (condition is "locked" or "inactive") await InScope(async (_, db) => { var u = await db.Users.SingleAsync(u => u.Id == f.Platform); if (condition == "locked") u.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); else u.IsActive = false; return await db.SaveChangesAsync(); });
        }
        var token = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        await Rejected(403, () => Incidents(s => s.ReviewClosureRiskAsync(f.Platform, f.Org.Id, i.Id, new(true, "Evaluacion", "Motivo", false, null, i.Revision, token))));
        Assert.Empty(await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id)));
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData("assessment")] [InlineData("length")] [InlineData("self")] [InlineData("justification")] [InlineData("context")] [InlineData("missing-context")]
    public async Task ReviewValidationNeverAdvancesIncidentOrCreatesPartialHistory(string invalid)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var token = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        var command = new ReviewClosureRisk(true, "Evaluacion", "Motivo", true, "Excepcion propia", i.Revision, token);
        command = invalid switch {
            "assessment" => command with { Assessment = " " }, "length" => command with { Assessment = new string('x', 4001) },
            "self" => command with { SelfReview = false, SelfReviewJustification = null },
            "justification" => command with { SelfReviewJustification = " " }, "context" => command with { ExpectedClosureContext = "bad" },
            _ => command with { ExpectedClosureContext = null! }
        };
        await Rejected(400, () => Incidents(s => s.ReviewClosureRiskAsync(f.Admin, f.Org.Id, i.Id, command)));
        Assert.Equal(i, await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)));
        Assert.Empty(await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id)));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task AnalystCanCloseAfterIndependentAdminReviewAndCannotReviewOwnRisk()
    {
        var f = await Seed(); var member = await Act(s => s.AddMemberAsync(f.Admin, f.Org.Id, new(f.Platform, OrganizationRole.Analyst)));
        var i = await ResolvedForClosure(f.Platform, f.Org.Id, member.Id, false, reviewed: false);
        var review = await RiskForTest(f.Admin, f.Org.Id, i); Assert.False(review.SelfReview); Assert.Null(review.SelfReviewJustification);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id));
        Assert.Equal("CLOSED", (await CloseWithContext(f.Platform, f.Org.Id, i, null)).Status);
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task ReopeningNeverReusesAcceptedRiskOrHistoricalClosureDecision()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        i = await CloseWithContext(f.Admin, f.Org.Id, i, null); i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.UnderEvaluation);
        i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.InResponse);
        i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.Resolved, "Resolucion despues de reapertura");
        Assert.False((await ClosureRead(f.Admin, f.Org.Id, i.Id)).RiskReview.AcceptanceCurrent);
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, i, null), "current_closure_risk_acceptance_required");
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData("update")] [InlineData("delete")] [InlineData("truncate")]
    public async Task RiskHistoryIsAppendOnlyInEfAndPostgres(string operation)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(operation switch {
            "update" => "UPDATE app.closure_risk_reviews SET assessment='Changed'", "delete" => "DELETE FROM app.closure_risk_reviews", _ => "TRUNCATE app.closure_risk_reviews"
        })));
        Assert.Equal("42501", error.SqlState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(async (_, db) => { var row = await db.ClosureRiskReviews.SingleAsync(); row.Assessment = "Changed"; return await db.SaveChangesAsync(); }));
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData("app.closure_risk_reviews")] [InlineData("app.incident_events")] [InlineData("audit.audit_events")]
    public async Task RiskReviewFailureRollsBackIncidentReviewAndSuccessHistories(string table)
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var count = await InScope((_, db) => db.IncidentEvents.CountAsync()); var name = "fail_risk_" + Guid.NewGuid().ToString("N");
        Assert.Contains(table, new[] { "app.closure_risk_reviews", "app.incident_events", "audit.audit_events" });
        // Identifiers are a generated UUID and the fixed fixture allowlist; PostgreSQL cannot parameterize identifiers.
        var install = $"CREATE FUNCTION app.{name}() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'Injected risk failure'; END; $f$; CREATE TRIGGER {name} BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION app.{name}();";
        var cleanup = $"DROP TRIGGER {name} ON {table}; DROP FUNCTION app.{name}();";
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(install));
        try { await Assert.ThrowsAnyAsync<Exception>(() => RiskForTest(f.Admin, f.Org.Id, i)); }
        finally { await InScope((_, db) => db.Database.ExecuteSqlRawAsync(cleanup)); }
        Assert.Equal(i, await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)));
        Assert.Equal(count, await InScope((_, db) => db.IncidentEvents.CountAsync())); Assert.Equal(0, await InScope((_, db) => db.ClosureRiskReviews.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Action == "incident.closure_risk_reviewed" && e.Outcome == "SUCCESS")));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task SqlCannotCloseWithoutRiskAcceptanceOrInsertUncorrelatedReview()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.incidents SET status='CLOSED',revision=revision+1 WHERE id={i.Id}")));
        Assert.Equal("23514", error.SqlState); Assert.Contains("risk acceptance", error.MessageText);
        var token = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).RiskReview.ContextToken;
        await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) => { db.ClosureRiskReviews.Add(new() { OrganizationId = f.Org.Id, IncidentId = i.Id, ActorUserId = f.Admin, CorrelationId = Guid.NewGuid(), IncidentRevision = i.Revision, ContextToken = token, Assessment = "Uncorrelated", Reason = "Uncorrelated", AcceptResidualRisk = true, SelfReview = true, SelfReviewJustification = "Own", OccurredAt = DateTimeOffset.UtcNow }); return await db.SaveChangesAsync(); }));
        Assert.Equal(0, await InScope((_, db) => db.ClosureRiskReviews.CountAsync()));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task DuplicateRiskReviewsSerializeWithoutPartialAcceptance()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var token = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        async Task<int> Attempt() { try { await Incidents(s => s.ReviewClosureRiskAsync(f.Admin, f.Org.Id, i.Id, new(true, "Evaluacion", "Motivo", true, "Propia", i.Revision, token))); return 200; }
            catch (IdentityOperationException e) { Assert.Equal(409, e.StatusCode); return 409; } }
        var results = await Race(Attempt, Attempt); Assert.Single(results, x => x == 200); Assert.Single(results, x => x == 409);
        Assert.Equal(1, await InScope((_, db) => db.ClosureRiskReviews.CountAsync()));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task RiskMigrationCanDowngradeEmptyAndRefusesToDropReviewHistory()
    {
        var f = await Seed(); var i = await RegisterIncident(f.Admin, f.Org.Id);
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261027031839_ExecutionSecurityHolds"); await db.Database.MigrateAsync(); return true; });
        i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, i);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261027031839_ExecutionSecurityHolds"); return true; }));
        Assert.Equal("23514", error.SqlState); Assert.Equal(1, await InScope((_, db) => db.ClosureRiskReviews.CountAsync()));
    }


    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task SqlRuntimeCanReviewAndCloseWithCurrentRiskWithoutMutationRightsToHistory()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var role = "openirp_risk_" + Guid.NewGuid().ToString("N"); var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)", owner)) { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            var report = await ClosureRead(f.Admin, f.Org.Id, i.Id);
            builder.Username = role; builder.Password = password; builder["Timezone"] = "America/Santiago";
            await using var runtime = new TestApplication(builder.ConnectionString); using var scope = runtime.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IIncidentService>();
            var state = await service.ClosureReadinessAsync(f.Admin, f.Org.Id, i.Id); Assert.Equal(report.RiskReview.ContextToken, state.RiskReview.ContextToken);
            var review = await service.ReviewClosureRiskAsync(f.Admin, f.Org.Id, i.Id, new(true, "Evaluacion runtime", "Motivo runtime", true, "Excepcion propia", i.Revision, state.ClosureContextToken));
            var current = await service.GetAsync(f.Admin, f.Org.Id, i.Id);
            Assert.Equal("CLOSED", (await service.TransitionAsync(f.Admin, f.Org.Id, i.Id, new(IncidentState.Closed, null, "Cierre runtime", current.Revision))).Status);
            var db = scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM app.closure_risk_reviews"))).SqlState);
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync(); }
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task RiskReviewAndHistoryStayScopedToTheExactOrganizationAndIncident()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false);
        var beta = await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var member = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == beta.Id));
        await Rejected(403, () => Incidents(s => s.ClosureRiskHistoryAsync(member.UserId, f.Org.Id, i.Id)));
        await Rejected(404, () => Incidents(s => s.ClosureRiskHistoryAsync(member.UserId, beta.Id, i.Id)));
        await Rejected(404, () => Incidents(s => s.ReviewClosureRiskAsync(member.UserId, beta.Id, i.Id, new(true, "Evaluacion", "Motivo", false, null, i.Revision, "bad"))));
        var other = await RegisterIncident(f.Admin, f.Org.Id); Assert.Empty(await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, other.Id)));
        foreach (var args in new[] { (-1L, 50), (0L, 0), (0L, 101) }) await Rejected(400, () => Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id, args.Item1, args.Item2)));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task ExistingClosedRowsRemainHistoricalAcrossMigrationAndRequireNewReviewAfterReopening()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        await InScope(async (_, db) => {
            await db.GetService<IMigrator>().MigrateAsync("20261027031839_ExecutionSecurityHolds");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.incidents SET status='CLOSED',revision=revision+1 WHERE id={i.Id}");
            await db.Database.MigrateAsync(); return true;
        });
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)); Assert.Equal("CLOSED", i.Status);
        Assert.Empty(await Incidents(s => s.ClosureRiskHistoryAsync(f.Admin, f.Org.Id, i.Id)));
        i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.UnderEvaluation);
        i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.InResponse);
        i = await MoveWorkflow(f.Admin, f.Org.Id, i, IncidentState.Resolved, "Nueva resolucion");
        await Rejected(409, () => CloseWithContext(f.Admin, f.Org.Id, i, null), "current_closure_risk_acceptance_required");
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task FileRecoveryInvalidatesRiskEvenWhenIncidentRevisionDoesNotChange()
    {
        var f = await ExecutionSeed();
        await WithFiles(async (runtime, root) => {
            var row = await UploadFile(runtime, f);
            var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
            Assert.True((await ClosureRead(f.Admin, f.Org, i.Id)).RiskReview.AcceptanceCurrent);
            await Files(runtime, s => s.RecoverFileAsync(f.Admin, f.Org, i.Id, row.File.EvidenceId, new(2, "Recuperar")));
            Assert.Equal(i.Revision, (await Incidents(s => s.GetAsync(f.Admin, f.Org, i.Id))).Revision);
            Assert.False((await ClosureRead(f.Admin, f.Org, i.Id)).RiskReview.AcceptanceCurrent);
            await Rejected(409, () => CloseWithContext(f.Admin, f.Org, i, null), "current_closure_risk_acceptance_required");
        }, failure: "publish_once");
    }

    [Theory, Trait("Feature", "ClosureRisk")]
    [InlineData(false)] [InlineData(true)]
    public async Task ReviewOnlyRunsOnResolvedClassifiedIncidentsAndCannotRewriteTerminalClosure(bool terminal)
    {
        var f = await Seed(); var i = terminal ? await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false) : await RegisterIncident(f.Admin, f.Org.Id);
        if (terminal) i = await CloseWithContext(f.Admin, f.Org.Id, i, null);
        i = await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)); // Compare the persisted PostgreSQL timestamp precision.
        var report = await ClosureRead(f.Admin, f.Org.Id, i.Id);
        await Rejected(409, () => Incidents(s => s.ReviewClosureRiskAsync(f.Admin, f.Org.Id, i.Id, new(true, "Evaluacion", "Motivo", false, null, i.Revision, report.ClosureContextToken))), "incident_not_resolved");
        Assert.Equal(i, await Incidents(s => s.GetAsync(f.Admin, f.Org.Id, i.Id)));
    }

    [Fact, Trait("Feature", "ClosureRisk")]
    public async Task RiskHttpRequiresExplicitDecisionsRevisionContextAndCsrf()
    {
        var f = await Seed(); var i = await ResolvedForClosure(f.Admin, f.Org.Id, f.Membership, false, reviewed: false);
        var path = $"/api/organizations/{f.Org.Id}/incidents/{i.Id}/closure-risk-reviews";
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, new { })).StatusCode);
        using var client = Client(); await Login(client, "alpha"); var token = (await ClosureRead(f.Admin, f.Org.Id, i.Id)).ClosureContextToken;
        var body = JsonSerializer.SerializeToElement(new ReviewClosureRisk(true, "Evaluacion", "Motivo", true, "Propia", i.Revision, token), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var missing in new[] { "acceptResidualRisk", "selfReview", "expectedIncidentRevision", "expectedClosureContext" })
        {
            var incomplete = body.EnumerateObject().Where(p => p.Name != missing).ToDictionary(p => p.Name, p => p.Value);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, incomplete)).StatusCode);
        }
        var csrf = client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single(); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, body)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, body)).StatusCode);
        using var response = await client.GetAsync(path); Assert.True(response.Headers.CacheControl!.NoStore); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
