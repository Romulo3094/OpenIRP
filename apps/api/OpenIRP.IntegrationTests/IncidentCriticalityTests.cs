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
    private static AssessIncidentCriticality CriticalityAssessment(long revision = 1) =>
        new(IncidentCriticality.High, null, "Evidencia de criticidad privada", revision);

    [Theory]
    [InlineData(IncidentCriticality.NotApplicable)]
    [InlineData(IncidentCriticality.Low)]
    [InlineData(IncidentCriticality.Medium)]
    [InlineData(IncidentCriticality.High)]
    public async Task ExplicitCriticalityValuesRoundTripWithoutInventingOriginalAssetCriticality(IncidentCriticality level)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        Assert.Null(incident.AssessedCriticality); Assert.Null(incident.CriticalityAssessedAtRevision);
        Assert.False(incident.IsCriticalityAssessmentComplete);
        var original = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var assessed = await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id,
            new(level, "Se justifica la aplicabilidad para este reporte", "Evaluación explícita", 2)));
        Assert.Equal(level, assessed.AssessedCriticality); Assert.Equal(3, assessed.CriticalityAssessedAtRevision);
        Assert.True(assessed.IsCriticalityAssessmentComplete);
        var reset = await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Evidencia insuficiente", 3)));
        Assert.Null(reset.AssessedCriticality); Assert.False(reset.IsCriticalityAssessmentComplete);
        Assert.Equal(4, reset.CriticalityAssessedAtRevision);
        Assert.Null((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Single().ReportedCriticality);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.Equal(level, history[2].AssessedCriticality); Assert.Equal(3, history[2].CriticalityAssessedAtRevision);
        Assert.Null(history[3].AssessedCriticality); Assert.Equal(original.Asset.Id, history[1].Details.GetProperty("asset").GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(AssetCriticality.High, IncidentCriticality.Low)]
    [InlineData(AssetCriticality.High, IncidentCriticality.Medium)]
    [InlineData(AssetCriticality.Medium, IncidentCriticality.Low)]
    [InlineData(AssetCriticality.Low, IncidentCriticality.NotApplicable)]
    public async Task LoweringKnownCriticalityRequiresSeparateJustificationAndKeepsOriginal(AssetCriticality reported, IncidentCriticality assessed)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported() with { ReportedCriticality = reported }));
        var request = new AssessIncidentCriticality(assessed, null, "Motivo general no sustituye reevaluación", 2);
        await Rejected(400, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, request)));
        await Rejected(400, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, request with { ReevaluationJustification = " " })));
        var item = await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id,
            request with { ReevaluationJustification = "  Contexto verificado: función sustituida y alcance delimitado  " }));
        Assert.Equal(assessed, item.AssessedCriticality);
        Assert.Equal(reported, (await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Single().ReportedCriticality);
        var decision = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last().Details.GetProperty("criticalityAssessment");
        Assert.Equal(reported.ToString(), decision.GetProperty("highestReportedCriticality").GetString());
        Assert.Equal("Contexto verificado: función sustituida y alcance delimitado", decision.GetProperty("reevaluationJustification").GetString());
    }

    [Fact]
    public async Task NotApplicableIsExplicitAndUnknownReportsStillRequireItsJustification()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var empty = await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(IncidentCriticality.NotApplicable, null, "No hay contexto de activos aplicable", 1)));
        Assert.Equal(IncidentCriticality.NotApplicable, empty.AssessedCriticality);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(2)));
        await Rejected(400, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(IncidentCriticality.NotApplicable, null, "No basta el motivo", 3))));
        Assert.Null((await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).AssessedCriticality);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("attach")]
    [InlineData("link")]
    public async Task AssetContextChangesInvalidateAssessmentWithoutOverwritingDecisionHistory(string change)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("Vínculo posterior")));
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(2)));
        if (change == "report") await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(3)));
        else if (change == "attach") await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(asset.Id, null, "Agregar activo", 3)));
        else await Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(asset.Id, "Identidad verificada", 3)));
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Null(current.AssessedCriticality); Assert.Null(current.CriticalityAssessedAtRevision); Assert.Equal(4, current.Revision);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.Equal(IncidentCriticality.High, history[2].AssessedCriticality); Assert.Null(history[3].AssessedCriticality);
        await Rejected(409, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(3))));
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(4)));
        var newSource = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last().Details.GetProperty("criticalityAssessment");
        var oldSource = history[2].Details.GetProperty("criticalityAssessment");
        Assert.NotEqual(oldSource.GetProperty("sourceDigestSha256").GetString(), newSource.GetProperty("sourceDigestSha256").GetString());
        Assert.Equal(4, newSource.GetProperty("assetContextRevision").GetInt64());
    }

    [Fact]
    public async Task AssessmentRetainsVersionedSourcesAndOtherIncidentMutationsKeepIt()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported() with { ReportedCriticality = AssetCriticality.High }));
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(2)));
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(3)));
        await Incidents(s => s.AssessImpactAsync(seed.Admin, seed.Org.Id, incident.Id, Cia(4)));
        await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment(5)));
        await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, incident.Id, new(incident.CategoryId, "Corregido", null, "Corrección", 6)));
        await Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, incident.Id, new(seed.Membership, "Asignar", 7)));
        var capability = await CapabilityId();
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin, seed.Org.Id, incident.Id, capability, Assessment(8)));
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(9)));
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.All(history.Skip(3), e => Assert.Equal(IncidentCriticality.High, e.AssessedCriticality));
        Assert.All(history.Skip(3).Take(6), e => Assert.Equal(4, e.CriticalityAssessedAtRevision));
        var source = history[3].Details.GetProperty("criticalityAssessment");
        Assert.Equal(2, source.GetProperty("assetCount").GetInt32()); Assert.Equal(1, source.GetProperty("unknownReportedCriticalityCount").GetInt32());
        Assert.Equal(3, source.GetProperty("assetContextRevision").GetInt64()); Assert.Equal("High", source.GetProperty("highestReportedCriticality").GetString());
        Assert.Equal(64, source.GetProperty("sourceDigestSha256").GetString()!.Length);
        Assert.Equal(source.GetProperty("sourceDigestSha256").GetString(), history.Last().Details.GetProperty("criticalityAssessment").GetProperty("sourceDigestSha256").GetString());
        var audit = await InScope((_, db) => db.AuditEvents.Where(e => e.Action == "incident.criticality_assessed").OrderBy(e => e.Id).FirstAsync());
        Assert.Equal(history[3].CorrelationId, audit.CorrelationId); Assert.DoesNotContain(CriticalityAssessment().Reason, JsonSerializer.Serialize(audit));
        Assert.Equal((await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))), (await Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id))).Items.Single());
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task AuthorizedWritersCanAssessCriticality(OrganizationRole role)
    {
        var seed = await Seed(); await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        Assert.True((await Incidents(s => s.AssessCriticalityAsync(seed.Platform, seed.Org.Id, incident.Id, CriticalityAssessment()))).IsCriticalityAssessmentComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerAndUnenrolledPlatformAdministratorCannotAssessCriticality(bool viewer)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (viewer) await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        await Rejected(403, () => Incidents(s => s.AssessCriticalityAsync(seed.Platform, seed.Org.Id, incident.Id, CriticalityAssessment())));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }


    [Theory]
    [InlineData(IncidentCriticality.Low, "direct")]
    [InlineData(IncidentCriticality.NotApplicable, "direct")]
    [InlineData(IncidentCriticality.Low, "reset")]
    [InlineData(IncidentCriticality.NotApplicable, "reset")]
    [InlineData(IncidentCriticality.Low, "asset")]
    [InlineData(IncidentCriticality.NotApplicable, "asset")]
    public async Task LoweringPreviousAssessmentRequiresJustificationEvenAfterUnknownOrInvalidation(IncidentCriticality value, string transition)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment()));
        if (transition == "reset") await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Nueva investigación", 2)));
        else if (transition == "asset") await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(2)));
        var request = new AssessIncidentCriticality(value, null, "Revisión de la evidencia", transition == "direct" ? 2 : 3);
        await Rejected(400, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, request)));
        var changed = await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id,
            request with { ReevaluationJustification = "La función crítica inicialmente sospechada fue descartada con evidencia" }));
        Assert.Equal(value, changed.AssessedCriticality);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.Equal(IncidentCriticality.High, history[1].AssessedCriticality);
    }
    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task CriticalityRequiresAnActiveAuthorizationContext(string condition)
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
        await Rejected(403, () => Incidents(s => s.AssessCriticalityAsync(seed.Platform, seed.Org.Id, incident.Id, CriticalityAssessment())));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task CriticalitySourcesAndAssessmentCannotCrossOrganizations()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var foreign = await RegisterIncident(betaAdmin, beta.Id);
        await Incidents(s => s.ReportAssetAsync(betaAdmin, beta.Id, foreign.Id, Reported() with { ReportedCriticality = AssetCriticality.High }));
        await Rejected(404, () => Incidents(s => s.AssessCriticalityAsync(betaAdmin, beta.Id, incident.Id, CriticalityAssessment())));
        await Rejected(403, () => Incidents(s => s.AssessCriticalityAsync(betaAdmin, seed.Org.Id, incident.Id, CriticalityAssessment())));
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(IncidentCriticality.NotApplicable, null, "Contexto propio sin activos", 1)));
        var source = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last().Details.GetProperty("criticalityAssessment");
        Assert.Equal(0, source.GetProperty("assetCount").GetInt32()); Assert.Equal(JsonValueKind.Null, source.GetProperty("highestReportedCriticality").ValueKind);
    }

    [Fact]
    public async Task InvalidCriticalityAndStaleRevisionDoNotAdvanceTheIncident()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        foreach (var request in new[] { CriticalityAssessment() with { AssessedCriticality = (IncidentCriticality)999 }, CriticalityAssessment() with { ReevaluationJustification = new string('x',2001) }, CriticalityAssessment() with { Reason = " " }, CriticalityAssessment() with { Reason = new string('x',2001) } })
            await Rejected(400, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, request)));
        await Rejected(409, () => Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(0))));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RacingCriticalityWithAnotherAssessmentOrAssetReportCannotUseStaleSources(bool report)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        async Task<int> Change(bool second)
        {
            try
            {
                if (second && report) await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
                else await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id,
                    second ? new(IncidentCriticality.Low, null, "Otra evaluación", 1) : CriticalityAssessment()));
                return 200;
            }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Change(false), () => Change(true))).Order().ToArray());
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        var last = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.Equal(2, current.Revision); Assert.Equal(current.AssessedCriticality, last.AssessedCriticality);
        Assert.Equal(current.CriticalityAssessedAtRevision, last.CriticalityAssessedAtRevision);
        if (last.Action == "ASSET_REPORTED") Assert.Null(current.AssessedCriticality);
        else Assert.Equal(0, last.Details.GetProperty("criticalityAssessment").GetProperty("assetCount").GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CriticalityAndItsInvalidationRollBackWhenEitherHistoryFails(bool assetChange, bool timeline)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (assetChange) await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment()));
        var revision = assetChange ? 2 : 1;
        await InScope((_, db) => db.Database.ExecuteSqlRawAsync(timeline ? """
            CREATE FUNCTION app.reject_criticality_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected criticality timeline failure'; END; $body$;
            CREATE TRIGGER reject_criticality_test BEFORE INSERT ON app.incident_events
            FOR EACH ROW EXECUTE FUNCTION app.reject_criticality_test();
            """ : """
            CREATE FUNCTION audit.reject_criticality_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected criticality audit failure'; END; $body$;
            CREATE TRIGGER reject_criticality_test BEFORE INSERT ON audit.audit_events
            FOR EACH ROW WHEN (NEW.outcome='SUCCESS') EXECUTE FUNCTION audit.reject_criticality_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (assetChange) await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(revision)));
            else await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, CriticalityAssessment(revision)));
        });
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Equal(revision, current.Revision);
        Assert.Equal(assetChange ? IncidentCriticality.High : (IncidentCriticality?)null, current.AssessedCriticality);
        Assert.Equal(revision, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Empty((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items);
    }

    [Theory]
    [InlineData(false, "enum")]
    [InlineData(false, "missing")]
    [InlineData(false, "future")]
    [InlineData(true, "enum")]
    [InlineData(true, "missing")]
    [InlineData(true, "future")]
    public async Task DatabaseChecksRejectInvalidCriticalityValuesOrRevisionReferences(bool historical, string invalid)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var value = invalid == "enum" ? (IncidentCriticality)999 : IncidentCriticality.High;
        long? reference = invalid == "missing" ? null : invalid == "enum" ? 2 : 999;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            if (historical) db.IncidentEvents.Add(new() { OrganizationId = seed.Org.Id, IncidentId = incident.Id, Revision = 2, ActorUserId = seed.Admin,
                Action = "CRITICALITY_ASSESSED", Reason = "Integridad SQL", CategoryId = incident.CategoryId, Title = "Inválido",
                AssessedCriticality = value, CriticalityAssessedAtRevision = reference });
            else { var row = await db.Incidents.SingleAsync(); row.Revision = 2; row.AssessedCriticality = value; row.CriticalityAssessedAtRevision = reference; }
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task CriticalityMigrationPreservesExistingContextAndRefusesEvidenceLossAfterReset()
    {
        var seed = await Seed(); var incident = await RegisterLegacyIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, ContextAssessment()));
        var before = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        await InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261023141918_IncidentContextAssessment"); await db.Database.MigrateAsync(); return true; });
        var after = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.OccurredAt, after.OccurredAt);
        Assert.Equal(before.Scope, after.Scope); Assert.Equal(before.OperationalImpact, after.OperationalImpact); Assert.Null(after.AssessedCriticality);
        await Incidents(s => s.AssessCriticalityAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Aún por investigar", 2)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) => { await db.GetService<IMigrator>().MigrateAsync("20261023141918_IncidentContextAssessment"); return true; }));
        Assert.Contains("Cannot roll back incident criticality", error.MessageText);
        Assert.Equal(3, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task CriticalityHttpRequiresExplicitValueRevisionAndCsrfWithoutAcceptingSeverityOrClosure()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var route = $"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/criticality";
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(route, CriticalityAssessment())).StatusCode);
        using var admin = Client(); await Login(admin, "alpha");
        var valid = JsonSerializer.SerializeToElement(CriticalityAssessment(), InventoryJson);
        foreach (var missing in new[] { "assessedCriticality", "expectedRevision" })
        {
            var body = valid.EnumerateObject().Where(p => p.Name != missing).ToDictionary(p => p.Name, p => (object)p.Value);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, body)).StatusCode);
        }
        foreach (var value in new object[] { "Invalid", 999 })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, new { assessedCriticality = value, reason = "Inválido", expectedRevision = 1 })).StatusCode);
        var response = await admin.PutAsJsonAsync(route, new { assessedCriticality = "NotApplicable", reason = "Sin contexto de activos aplicable", expectedRevision = 1, severity = "Critical", maximumSeverityReached = "Critical", status = "CLOSED" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var item = JsonSerializer.Deserialize<IncidentItem>(text, InventoryJson)!;
        Assert.Equal(IncidentCriticality.NotApplicable, item.AssessedCriticality); Assert.Equal(2, item.CriticalityAssessedAtRevision);
        Assert.Equal("REGISTERED", item.Status); Assert.Equal(JsonValueKind.Null, JsonSerializer.Deserialize<JsonElement>(text).GetProperty("severity").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(route, CriticalityAssessment())).StatusCode);
        var listed = (await admin.GetFromJsonAsync<IncidentPage<IncidentItem>>($"/api/organizations/{seed.Org.Id}/incidents", InventoryJson))!.Items.Single();
        Assert.Equal(item.AssessedCriticality, listed.AssessedCriticality);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(route, CriticalityAssessment(2))).StatusCode);
    }
}
