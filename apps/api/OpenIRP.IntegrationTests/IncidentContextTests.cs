using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private static AssessIncidentContext ContextAssessment(long revision = 1) =>
        new(IncidentScope.OrganizationWide, OperationalImpact.CriticalInterruption, "Evidencia operacional privada", revision);

    [Fact]
    public async Task ContextStartsUnknownAndExplicitNoneIsDifferentFromMissingEvidence()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        Assert.Null(incident.Scope); Assert.Null(incident.OperationalImpact); Assert.False(incident.IsContextAssessmentComplete);
        var partial = await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id,
            new(null, OperationalImpact.None, "Operación sin degradación; alcance por investigar", 1)));
        Assert.Null(partial.Scope); Assert.Equal(OperationalImpact.None, partial.OperationalImpact);
        Assert.False(partial.IsContextAssessmentComplete); Assert.Equal(2, partial.Revision);
        Assert.False(partial.IsImpactAssessmentComplete); Assert.Equal("REGISTERED", partial.Status);
    }

    [Theory]
    [InlineData(IncidentScope.Single, OperationalImpact.None)]
    [InlineData(IncidentScope.Single, OperationalImpact.Degraded)]
    [InlineData(IncidentScope.Single, OperationalImpact.MajorInterruption)]
    [InlineData(IncidentScope.Single, OperationalImpact.CriticalInterruption)]
    [InlineData(IncidentScope.Multiple, OperationalImpact.None)]
    [InlineData(IncidentScope.Multiple, OperationalImpact.Degraded)]
    [InlineData(IncidentScope.Multiple, OperationalImpact.MajorInterruption)]
    [InlineData(IncidentScope.Multiple, OperationalImpact.CriticalInterruption)]
    [InlineData(IncidentScope.OrganizationWide, OperationalImpact.None)]
    [InlineData(IncidentScope.OrganizationWide, OperationalImpact.Degraded)]
    [InlineData(IncidentScope.OrganizationWide, OperationalImpact.MajorInterruption)]
    [InlineData(IncidentScope.OrganizationWide, OperationalImpact.CriticalInterruption)]
    public async Task ContextValuesRoundTripAndResetPreservesEvidence(IncidentScope scope, OperationalImpact impact)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var assessed = await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id,
            new(scope, impact, "Contexto confirmado", 1)));
        Assert.Equal(scope, assessed.Scope); Assert.Equal(impact, assessed.OperationalImpact);
        Assert.True(assessed.IsContextAssessmentComplete);
        var reset = await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id,
            new(null, null, "Nueva evidencia exige reevaluar", 2)));
        Assert.False(reset.IsContextAssessmentComplete); Assert.Null(reset.Scope); Assert.Null(reset.OperationalImpact);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.Null(history[0].Scope); Assert.Equal(scope, history[1].Scope); Assert.Equal(impact, history[1].OperationalImpact);
        Assert.Null(history[2].Scope); Assert.Null(history[2].OperationalImpact);
    }

    [Fact]
    public async Task LaterMutationsPreserveContextAndDoNotInferScopeFromAssetCount()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id); var capability = await CapabilityId();
        await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment()));
        await Incidents(s => s.AssessImpactAsync(seed.Admin, seed.Org.Id, incident.Id, Cia(2)));
        await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, incident.Id, new(incident.CategoryId, "Corrección", null, "Corregir", 3)));
        await Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, incident.Id, new(seed.Membership, "Asignar", 4)));
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(5)));
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin, seed.Org.Id, incident.Id, capability, Assessment(6)));
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.All(history.Skip(1), e => { Assert.Equal(IncidentScope.OrganizationWide, e.Scope); Assert.Equal(OperationalImpact.CriticalInterruption, e.OperationalImpact); });
        var item = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        var listed = (await Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id))).Items.Single();
        Assert.Equal(item, listed); Assert.Equal(ImpactLevel.High, item.ConfidentialityImpact); Assert.Equal(7, item.Revision);
        Assert.Single((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.context_assessed"));
        Assert.Equal(history[1].CorrelationId, audit.CorrelationId); Assert.Equal(incident.Id, audit.ResourceId);
        Assert.DoesNotContain(ContextAssessment().Reason, JsonSerializer.Serialize(audit));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync("UPDATE app.incident_events SET scope='SINGLE'")));
        Assert.Equal("42501", error.SqlState);
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ActiveIncidentWritersCanAssessContext(OrganizationRole role)
    {
        var seed = await Seed(); await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        Assert.True((await Incidents(s => s.AssessContextAsync(seed.Platform, seed.Org.Id, incident.Id, ContextAssessment()))).IsContextAssessmentComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerAndUnenrolledPlatformAdministratorCannotAssessContext(bool viewer)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (viewer) { await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer))); await Incidents(s => s.GetAsync(seed.Platform, seed.Org.Id, incident.Id)); }
        await Rejected(403, () => Incidents(s => s.AssessContextAsync(seed.Platform, seed.Org.Id, incident.Id, ContextAssessment())));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task ContextWritesRequireCurrentActiveAuthorization(string condition)
    {
        var seed = await Seed(); var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Analyst)));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (condition == "membership") await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.Analyst, MembershipStatus.Suspended, null)));
        else await InScope(async (_, db) =>
        {
            if (condition == "organization") (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended;
            else { var user = await db.Users.SingleAsync(u => u.Id == seed.Platform); if (condition == "account") user.IsActive = false; else user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15); }
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Incidents(s => s.AssessContextAsync(seed.Platform, seed.Org.Id, incident.Id, ContextAssessment())));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task ContextCannotBeWrittenAcrossOrganizations()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        await Rejected(404, () => Incidents(s => s.AssessContextAsync(betaAdmin, beta.Id, incident.Id, ContextAssessment())));
        await Rejected(403, () => Incidents(s => s.AssessContextAsync(betaAdmin, seed.Org.Id, incident.Id, ContextAssessment())));
        Assert.Null((await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Scope);
    }

    [Fact]
    public async Task InvalidContextAndStaleRevisionCannotChangeEvidence()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        foreach (var request in new[] { ContextAssessment() with { Scope = (IncidentScope)999 }, ContextAssessment() with { OperationalImpact = (OperationalImpact)999 }, ContextAssessment() with { Reason = " " }, ContextAssessment() with { Reason = new string('x', 2001) } })
            await Rejected(400, () => Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, request)));
        await Rejected(409, () => Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment(0))));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentContextAssessmentsCannotMixInputsOrOverwriteSameRevision()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        async Task<int> Assess(bool second)
        {
            try { await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, second ? new(IncidentScope.Single, OperationalImpact.None, "Segunda evidencia", 1) : ContextAssessment())); return 200; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Assess(false), () => Assess(true))).Order().ToArray());
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        var snapshot = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items[1];
        Assert.Equal(current.Scope, snapshot.Scope); Assert.Equal(current.OperationalImpact, snapshot.OperationalImpact);
        Assert.True((current.Scope == IncidentScope.Single && current.OperationalImpact == OperationalImpact.None)
            || (current.Scope == IncidentScope.OrganizationWide && current.OperationalImpact == OperationalImpact.CriticalInterruption));
        Assert.Equal(2, current.Revision);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailureOfContextTimelineOrAuditRollsBackBothInputs(bool reassessment, bool timeline)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (reassessment) await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment()));
        var revision = reassessment ? 2 : 1;
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(timeline ? """
            CREATE FUNCTION app.reject_context_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected context timeline failure'; END; $body$;
            CREATE TRIGGER reject_context_test BEFORE INSERT ON app.incident_events FOR EACH ROW
            WHEN (NEW.action='CONTEXT_ASSESSED') EXECUTE FUNCTION app.reject_context_test();
            """ : """
            CREATE FUNCTION audit.reject_context_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected context audit failure'; END; $body$;
            CREATE TRIGGER reject_context_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS' AND NEW.action='incident.context_assessed') EXECUTE FUNCTION audit.reject_context_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(() => Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Revisar evidencia", revision))));
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Equal(revision, current.Revision);
        Assert.Equal(reassessment ? IncidentScope.OrganizationWide : (IncidentScope?)null, current.Scope);
        Assert.Equal(reassessment ? OperationalImpact.CriticalInterruption : (OperationalImpact?)null, current.OperationalImpact);
        Assert.Equal(revision, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "FAILURE")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DatabaseChecksRejectInvalidContextOnCurrentAndHistoricalRows(bool historical, bool scope)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            if (historical) db.IncidentEvents.Add(new() { OrganizationId = seed.Org.Id, IncidentId = incident.Id, Revision = 2, ActorUserId = seed.Admin,
                Action = "CONTEXT_ASSESSED", Reason = "Integridad SQL", CategoryId = incident.CategoryId, Title = "Contexto inválido",
                Scope = scope ? (IncidentScope)999 : null, OperationalImpact = scope ? null : (OperationalImpact)999 });
            else { var row = await db.Incidents.SingleAsync(); if (scope) row.Scope = (IncidentScope)999; else row.OperationalImpact = (OperationalImpact)999; }
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task ContextMigrationPreservesOldHistoryAndRejectsDowngradeAfterReset()
    {
        var seed = await Seed(); var incident = await RegisterLegacyIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.AssessImpactAsync(seed.Admin, seed.Org.Id, incident.Id, Cia()));
        var before = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261023032311_IncidentImpactAssessment"); await db.Database.MigrateAsync(); return true; });
        var after = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.OccurredAt, after.OccurredAt); Assert.Equal(before.ConfidentialityImpact, after.ConfidentialityImpact);
        Assert.Null(after.Scope); Assert.Null(after.OperationalImpact);
        await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment(2)));
        await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Volver a investigar", 3)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261023032311_IncidentImpactAssessment"); return true; }));
        Assert.Contains("Cannot roll back incident context", error.MessageText);
        Assert.Equal(4, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task HttpContextRequiresExplicitInputsCsrfAndRevisionAndCannotSetSeverityOrState()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var route = $"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/context";
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(route, ContextAssessment())).StatusCode);
        using var admin = Client(); await Login(admin, "alpha");
        var valid = JsonSerializer.SerializeToElement(ContextAssessment(), InventoryJson);
        foreach (var missing in new[] { "scope", "operationalImpact", "expectedRevision" })
        {
            var body = valid.EnumerateObject().Where(p => p.Name != missing).ToDictionary(p => p.Name, p => (object)p.Value);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, body)).StatusCode);
        }
        foreach (var value in new object[] { "Invalid", 999 })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, new { scope = value, operationalImpact = "None", reason = "Inválido", expectedRevision = 1 })).StatusCode);
        var response = await admin.PutAsJsonAsync(route, new { scope = (string?)null, operationalImpact = "None", reason = "Operación comprobada", expectedRevision = 1, severity = "Critical", maximumSeverityReached = "Critical", status = "CLOSED" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bodyText = await response.Content.ReadAsStringAsync();
        var item = JsonSerializer.Deserialize<IncidentItem>(bodyText, InventoryJson)!;
        Assert.Null(item.Scope); Assert.Equal(OperationalImpact.None, item.OperationalImpact); Assert.False(item.IsContextAssessmentComplete);
        Assert.Equal("REGISTERED", item.Status);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.Deserialize<JsonElement>(bodyText).GetProperty("severity").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(route, ContextAssessment())).StatusCode);
        var listed = (await admin.GetFromJsonAsync<IncidentPage<IncidentItem>>($"/api/organizations/{seed.Org.Id}/incidents", InventoryJson))!.Items.Single();
        Assert.Null(listed.Scope); Assert.Equal(OperationalImpact.None, listed.OperationalImpact);
        var history = await admin.GetFromJsonAsync<IncidentEventPage>($"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/events", InventoryJson);
        Assert.Equal(OperationalImpact.None, history!.Items.Last().OperationalImpact);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, ContextAssessment(2))).StatusCode);
    }
}
