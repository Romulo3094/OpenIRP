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
using OpenIRP.Domain.Severity;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<IncidentItem> CompleteSeverityInputs(Guid actor, Guid organization, IncidentItem incident,
        ImpactLevel cia = ImpactLevel.High, IncidentCriticality criticality = IncidentCriticality.High)
    {
        var current = await Incidents(s => s.AssessImpactAsync(actor, organization, incident.Id,
            new(cia, ImpactLevel.None, ImpactLevel.None, "Evaluación CIA", incident.Revision)));
        current = await Incidents(s => s.AssessContextAsync(actor, organization, incident.Id,
            new(IncidentScope.Single, OperationalImpact.None, "Contexto observado", current.Revision)));
        return await Incidents(s => s.AssessCriticalityAsync(actor, organization, incident.Id,
            new(criticality, "Contexto de activos confirmado", "Criticidad evaluada", current.Revision)));
    }

    [Fact]
    public async Task NewIncidentsStartPendingWithVersionedInputsAndNoAssumedMaximum()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        Assert.Equal("Pending", incident.ClassificationStatus); Assert.Null(incident.Severity); Assert.Null(incident.MaximumSeverityReached);
        Assert.Equal(SeverityRuleV1.Version, incident.SeverityRuleVersion); Assert.Equal(1, incident.SeverityAssessedAtRevision);
        var first = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Single();
        var classification = first.Details.GetProperty("classification");
        Assert.Equal("INCOMPLETE_INPUTS", classification.GetProperty("matchedRule").GetString());
        Assert.Equal(JsonValueKind.Null, classification.GetProperty("inputs").GetProperty("confidentiality").ValueKind);
        Assert.Equal(incident.SeverityRuleVersion, first.SeverityRuleVersion);
    }

    [Theory]
    [InlineData(ImpactLevel.None, IncidentCriticality.NotApplicable, IncidentSeverity.Low)]
    [InlineData(ImpactLevel.Low, IncidentCriticality.High, IncidentSeverity.Medium)]
    [InlineData(ImpactLevel.High, IncidentCriticality.Low, IncidentSeverity.High)]
    [InlineData(ImpactLevel.High, IncidentCriticality.High, IncidentSeverity.Critical)]
    public async Task CompletedInputsCalculateServerSeverityAndHistoricalMaximum(ImpactLevel cia, IncidentCriticality criticality, IncidentSeverity expected)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var current = await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident, cia, criticality);
        Assert.Equal(expected, current.Severity); Assert.Equal(expected, current.MaximumSeverityReached);
        Assert.Equal("Classified", current.ClassificationStatus); Assert.Equal(4, current.SeverityAssessedAtRevision);
        var last = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.Equal(expected, last.Severity); Assert.Equal(expected, last.MaximumSeverityReached);
        Assert.Equal(criticality.ToString(), last.Details.GetProperty("classification").GetProperty("inputs").GetProperty("criticality").GetString());
        Assert.True(last.Details.TryGetProperty("criticalityAssessment", out _));
        Assert.Equal(current, (await Incidents(s => s.ListAsync(seed.Admin, seed.Org.Id))).Items.Single());
    }

    [Fact]
    public async Task SeverityMayDecreaseOrBecomePendingButHistoricalMaximumCannot()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident);
        var lower = await Incidents(s => s.AssessImpactAsync(seed.Admin, seed.Org.Id, incident.Id, new(ImpactLevel.None, ImpactLevel.None, ImpactLevel.None, "Impacto descartado con evidencia", 4)));
        Assert.Equal(IncidentSeverity.Low, lower.Severity); Assert.Equal(IncidentSeverity.Critical, lower.MaximumSeverityReached);
        var pending = await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, new(null, null, "Reinvestigar alcance", 5)));
        Assert.Null(pending.Severity); Assert.Equal("Pending", pending.ClassificationStatus);
        Assert.Equal(IncidentSeverity.Critical, pending.MaximumSeverityReached);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.Equal(IncidentSeverity.Critical, history[3].Severity);
        Assert.Equal(IncidentSeverity.Low, history[4].Severity); Assert.Null(history[5].Severity);
        Assert.All(history.Skip(3), e => Assert.Equal(IncidentSeverity.Critical, e.MaximumSeverityReached));
    }

    [Theory]
    [InlineData("report")]
    [InlineData("attach")]
    [InlineData("link")]
    public async Task AssetMutationsInvalidateCurrentSeverityAndRetainHistoricalMaximum(string operation)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("Nuevo contexto")));
        var current = await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident with { Revision = 2 });
        Assert.Equal(IncidentSeverity.Critical, current.Severity);
        if (operation == "report") await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(5)));
        else if (operation == "attach") await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(asset.Id, null, "Adjuntar", 5)));
        else await Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(asset.Id, "Vincular", 5)));
        current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Null(current.Severity); Assert.Null(current.AssessedCriticality); Assert.Equal(IncidentSeverity.Critical, current.MaximumSeverityReached);
        Assert.Equal(6, current.SeverityAssessedAtRevision);
        var last = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.True(last.Details.TryGetProperty("asset", out _)); Assert.True(last.Details.TryGetProperty("classification", out _));
    }

    [Fact]
    public async Task OrdinaryMutationsPreserveClassificationAndReferenceToActualCalculation()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident);
        await Incidents(s => s.UpdateAsync(seed.Admin, seed.Org.Id, incident.Id, new(incident.CategoryId, "Corrección", null, "Título", 4)));
        await Incidents(s => s.AssignAsync(seed.Admin, seed.Org.Id, incident.Id, new(seed.Membership, "Asignar", 5)));
        var capability = await CapabilityId();
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin, seed.Org.Id, incident.Id, capability, Assessment(6)));
        var last = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Last();
        Assert.Equal(IncidentSeverity.Critical, last.Severity); Assert.Equal(4, last.SeverityAssessedAtRevision);
        Assert.Equal(7, last.Revision); Assert.Equal("REGISTERED", last.Status);
    }

    [Fact]
    public async Task ExistingIncidentsRequireExplicitFirstCalculationAndNeverRewriteEarlierEvents()
    {
        var seed = await Seed(); var incident = await RegisterLegacyIncident(seed.Admin, seed.Org.Id);
        Assert.Equal("NotEvaluated", incident.ClassificationStatus);
        var ready = await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident);
        Assert.Null(ready.Severity); Assert.Null(ready.MaximumSeverityReached); Assert.Equal("NotEvaluated", ready.ClassificationStatus);
        var before = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        var assessed = await Incidents(s => s.AssessSeverityAsync(seed.Admin, seed.Org.Id, incident.Id, new("Primera clasificación explícita", 4)));
        Assert.Equal(IncidentSeverity.Critical, assessed.Severity); Assert.Equal(IncidentSeverity.Critical, assessed.MaximumSeverityReached);
        var after = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items;
        Assert.All(after.Take(4), e => { Assert.Null(e.Severity); Assert.Null(e.SeverityRuleVersion); });
        Assert.Equal(before.Select(e => e.Id), after.Take(4).Select(e => e.Id));
        Assert.Equal(before.Select(e => e.Details.GetRawText()), after.Take(4).Select(e => e.Details.GetRawText()));
        Assert.Equal(5, assessed.SeverityAssessedAtRevision);
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.severity_assessed"));
        Assert.Equal(after.Last().CorrelationId, audit.CorrelationId);
        Assert.DoesNotContain("Primera clasificación explícita", JsonSerializer.Serialize(audit));
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ActiveIncidentWritersCanReevaluateSeverity(OrganizationRole role)
    {
        var seed = await Seed(); await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var result = await Incidents(s => s.AssessSeverityAsync(seed.Platform, seed.Org.Id, incident.Id, new("Revisar", 1)));
        Assert.Equal("Pending", result.ClassificationStatus); Assert.Equal(2, result.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerAndUnenrolledPlatformAdministratorCannotReevaluateSeverity(bool viewer)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        if (viewer) await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
        await Rejected(403, () => Incidents(s => s.AssessSeverityAsync(seed.Platform, seed.Org.Id, incident.Id, new("Revisar", 1))));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task SeverityReevaluationRequiresActiveAuthorizationContext(string condition)
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
        await Rejected(403, () => Incidents(s => s.AssessSeverityAsync(seed.Platform, seed.Org.Id, incident.Id, new("Revisar", 1))));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task SeverityCannotCrossOrganizationBoundary()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        await Rejected(404, () => Incidents(s => s.AssessSeverityAsync(betaAdmin, beta.Id, incident.Id, new("Revisar", 1))));
        await Rejected(403, () => Incidents(s => s.AssessSeverityAsync(betaAdmin, seed.Org.Id, incident.Id, new("Revisar", 1))));
    }

    [Fact]
    public async Task SeverityRequiresReasonAndCurrentRevision()
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        foreach (var reason in new[] { " ", new string('x',2001) })
            await Rejected(400, () => Incidents(s => s.AssessSeverityAsync(seed.Admin, seed.Org.Id, incident.Id, new(reason, 1))));
        await Rejected(409, () => Incidents(s => s.AssessSeverityAsync(seed.Admin, seed.Org.Id, incident.Id, new("Revisar", 0))));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCalculationsCannotMixInputsOrCommitAnObsoleteRevision(bool explicitReevaluation)
    {
        var seed = await Seed(); var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin, seed.Org.Id, incident, ImpactLevel.None, IncidentCriticality.Low);
        async Task<int> Change(bool second)
        {
            try
            {
                if (second && explicitReevaluation) await Incidents(s => s.AssessSeverityAsync(seed.Admin, seed.Org.Id, incident.Id, new("Revisar", 4)));
                else if (second) await Incidents(s => s.AssessContextAsync(seed.Admin, seed.Org.Id, incident.Id, new(IncidentScope.Single, OperationalImpact.CriticalInterruption, "Operación crítica", 4)));
                else await Incidents(s => s.AssessImpactAsync(seed.Admin, seed.Org.Id, incident.Id, new(ImpactLevel.High, ImpactLevel.None, ImpactLevel.None, "Impacto confirmado", 4)));
                return 200;
            }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Change(false), () => Change(true))).Order().ToArray());
        var current = await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id));
        var expected = SeverityRuleV1.Evaluate(new(current.ConfidentialityImpact,current.IntegrityImpact,current.AvailabilityImpact,current.AssessedCriticality,current.Scope,current.OperationalImpact));
        Assert.Equal(expected.Severity,current.Severity); Assert.Equal(current.Severity,current.MaximumSeverityReached); Assert.Equal(5,current.Revision);
        var snapshot=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Last();
        Assert.Equal(current.Severity,snapshot.Severity); Assert.Equal(5,snapshot.SeverityAssessedAtRevision);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ClassificationAndMaximumRollBackWhenEitherHistoryFails(bool firstLegacyCalculation, bool timeline)
    {
        var seed=await Seed();
        var incident= firstLegacyCalculation ? await RegisterLegacyIncident(seed.Admin,seed.Org.Id) : await RegisterIncident(seed.Admin,seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin,seed.Org.Id,incident,ImpactLevel.None,IncidentCriticality.Low);
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(timeline ? """
            CREATE FUNCTION app.reject_severity_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected severity timeline failure'; END; $body$;
            CREATE TRIGGER reject_severity_test BEFORE INSERT ON app.incident_events FOR EACH ROW
            EXECUTE FUNCTION app.reject_severity_test();
            """ : """
            CREATE FUNCTION audit.reject_severity_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected severity audit failure'; END; $body$;
            CREATE TRIGGER reject_severity_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS') EXECUTE FUNCTION audit.reject_severity_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(async ()=>
        {
            if(firstLegacyCalculation) await Incidents(s=>s.AssessSeverityAsync(seed.Admin,seed.Org.Id,incident.Id,new("Primer cálculo",4)));
            else await Incidents(s=>s.AssessContextAsync(seed.Admin,seed.Org.Id,incident.Id,new(IncidentScope.Single,OperationalImpact.CriticalInterruption,"Nueva interrupción",4)));
        });
        var current=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id));
        Assert.Equal(4,current.Revision);
        Assert.Equal(firstLegacyCalculation ? null : IncidentSeverity.Low,current.MaximumSeverityReached);
        Assert.Equal(firstLegacyCalculation ? null : IncidentSeverity.Low,current.Severity);
        Assert.Equal(firstLegacyCalculation ? null : SeverityRuleV1.Version,current.SeverityRuleVersion);
        Assert.Equal(4,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Theory]
    [InlineData("UPDATE app.incidents SET maximum_severity_reached='LOW',severity='LOW'")]
    [InlineData("UPDATE app.incidents SET maximum_severity_reached=NULL,severity=NULL")]
    public async Task DatabaseRejectsHistoricalMaximumReductionEvenIfCurrentSeverityIsLowered(string sql)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin,seed.Org.Id,incident);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("23514",error.SqlState);
        Assert.Equal(IncidentSeverity.Critical,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).MaximumSeverityReached);
    }

    [Theory]
    [InlineData("higher")]
    [InlineData("invalid")]
    [InlineData("orphan")]
    [InlineData("future")]
    public async Task DatabaseChecksEnforceSeverityMetadataAndMaximumConsistency(string invalid)
    {
        var seed=await Seed();await RegisterIncident(seed.Admin,seed.Org.Id);
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async (_,db)=>
        {
            var row=await db.Incidents.SingleAsync();row.Revision=2;row.SeverityAssessedAtRevision=2;
            row.Severity=invalid=="invalid" ? (IncidentSeverity)99 : IncidentSeverity.High;
            row.MaximumSeverityReached=invalid=="higher" ? IncidentSeverity.Low : IncidentSeverity.High;
            if(invalid=="orphan")row.SeverityRuleVersion=null;
            if(invalid=="future")row.SeverityAssessedAtRevision=999;
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task SeverityMigrationPreservesEarlierEventsAndRefusesLossEvenForPendingClassification()
    {
        var seed=await Seed();var incident=await RegisterLegacyIncident(seed.Admin,seed.Org.Id);
        var before=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        await InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261023200334_IncidentCriticalityAssessment");await db.Database.MigrateAsync();return true;});
        var after=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        Assert.Equal(before.Id,after.Id);Assert.Equal(before.OccurredAt,after.OccurredAt);
        Assert.Null(after.Severity);Assert.Null(after.SeverityRuleVersion);
        await Incidents(s=>s.AssessSeverityAsync(seed.Admin,seed.Org.Id,incident.Id,new("Evaluación aún incompleta",1)));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261023200334_IncidentCriticalityAssessment");return true;}));
        Assert.Contains("Cannot roll back incident severity",error.MessageText);
        Assert.Equal(2,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task HttpSeverityUsesServerInputsAndRejectsMissingRevisionCsrfAndClientSuppliedLevels()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        await CompleteSeverityInputs(seed.Admin,seed.Org.Id,incident);
        var route=$"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/severity";
        using var anonymous=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PutAsJsonAsync(route,new{reason="Revisar",expectedRevision=4})).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(route,new{reason="Falta revisión"})).StatusCode);
        var response=await admin.PutAsJsonAsync(route,new{reason="Reevaluación HTTP",expectedRevision=4,severity="Low",maximumSeverityReached="Low",severityRuleVersion="forged",status="CLOSED"});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var item=await response.Content.ReadFromJsonAsync<IncidentItem>(InventoryJson);
        Assert.Equal(IncidentSeverity.Critical,item!.Severity);Assert.Equal(IncidentSeverity.Critical,item.MaximumSeverityReached);
        Assert.Equal(SeverityRuleV1.Version,item.SeverityRuleVersion);Assert.Equal("REGISTERED",item.Status);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(route,new{reason="Obsoleto",expectedRevision=4})).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(route,new{reason="Sin CSRF",expectedRevision=5})).StatusCode);
    }
}
