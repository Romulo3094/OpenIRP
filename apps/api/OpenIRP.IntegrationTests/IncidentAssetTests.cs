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
    private static ReportIncidentAsset Reported(long revision = 1) => new("Estación reportada", AssetKind.Workstation,
        "Contexto privado del reporte", null, "Reporte inicial", revision);
    private async Task<IncidentItem> RegisterIncident(Guid actor, Guid org)
    {
        var request = await NewIncident();
        return await Incidents(s => s.CreateAsync(actor, org, request));
    }

    [Fact]
    public async Task UninventoriedReportKeepsUnknownCriticalityAndAtomicPrivateHistory()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var result = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        Assert.Equal(2, result.IncidentRevision);
        Assert.Equal(2, result.Asset.ReportedAtRevision);
        Assert.Null(result.Asset.AssetId); Assert.Null(result.Asset.LinkedAtRevision); Assert.Null(result.Asset.ReportedCriticality);
        Assert.Equal(0, await InScope((_, db) => db.Assets.CountAsync()));
        var events = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id));
        var report = events.Items[1];
        Assert.Equal("ASSET_REPORTED", report.Action);
        Assert.Equal("Estación reportada", report.Details.GetProperty("asset").GetProperty("reportedName").GetString());
        Assert.Equal(JsonValueKind.Null, report.Details.GetProperty("asset").GetProperty("reportedCriticality").ValueKind);
        Assert.Equal("Workstation", report.Details.GetProperty("asset").GetProperty("reportedType").GetString());
        var audit = await InScope((_, db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.asset_reported"));
        Assert.Equal(report.CorrelationId, audit.CorrelationId);
        Assert.DoesNotContain(Reported().ReportedName, JsonSerializer.Serialize(audit));
        Assert.DoesNotContain(Reported().ReportedContext!, JsonSerializer.Serialize(audit));
    }

    [Fact]
    public async Task LaterInventoryLinkPreservesOriginalReportAndEarlierEvent()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("Nombre inventariado distinto")));
        var linked = await Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(asset.Id, "Identidad verificada", 2)));
        Assert.Equal(3, linked.IncidentRevision);
        Assert.Equal(3, linked.Asset.LinkedAtRevision);
        Assert.Equal(asset.Id, linked.Asset.AssetId);
        Assert.Equal(report.Asset.ReportedName, linked.Asset.ReportedName);
        Assert.Equal(report.Asset.ReportedType, linked.Asset.ReportedType);
        Assert.Equal(report.Asset.ReportedContext, linked.Asset.ReportedContext);
        Assert.Null(linked.Asset.ReportedCriticality);
        Assert.Equal(report.Asset.ReportedAtRevision, linked.Asset.ReportedAtRevision);
        var events = await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Equal(JsonValueKind.Null, events.Items[1].Details.GetProperty("asset").GetProperty("assetId").ValueKind);
        Assert.Equal(asset.Id, events.Items[2].Details.GetProperty("asset").GetProperty("assetId").GetGuid());
        await Rejected(409, () => Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(asset.Id, "Intentar reemplazo", 3))), "incident_asset_already_linked");
        Assert.Equal(3, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Fact]
    public async Task AttachedInventorySnapshotSurvivesRenameReclassificationAndRetirement()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        var attached = await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(asset.Id, "Contexto inicial", "Identificado", 1)));
        Assert.Equal(AssetCriticality.High, attached.Asset.ReportedCriticality);
        Assert.Equal(2, attached.Asset.LinkedAtRevision);
        await Inventory(s => s.UpdateAssetAsync(seed.Admin, seed.Org.Id, asset.Id, new("Nombre nuevo", AssetKind.CloudService, AssetCriticality.Low, null, AssetStatus.Active, 1)));
        await Inventory(s => s.RetireAssetAsync(seed.Admin, seed.Org.Id, asset.Id, 2));
        var stored = Assert.Single((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items);
        Assert.Equal(asset.Name, stored.ReportedName);
        Assert.Equal(AssetKind.Server, stored.ReportedType);
        Assert.Equal(AssetCriticality.High, stored.ReportedCriticality);
        var other = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Rejected(409, () => Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, other.Id, new(asset.Id, null, "Retirado", 1))), "inactive_asset");
    }

    [Fact]
    public async Task InventoryAssetCanAppearInDifferentIncidentsButOnlyOncePerIncident()
    {
        var seed = await Seed();
        var first = await RegisterIncident(seed.Admin, seed.Org.Id);
        var second = await RegisterIncident(seed.Admin, seed.Org.Id);
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, first.Id, new(asset.Id, null, "Primero", 1)));
        await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, second.Id, new(asset.Id, null, "Segundo", 1)));
        await Rejected(409, () => Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, first.Id, new(asset.Id, null, "Duplicado", 2))), "asset_already_in_incident");
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, first.Id, Reported(2)));
        await Rejected(409, () => Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, first.Id, report.Asset.Id, new(asset.Id, "Duplicado", 3))), "asset_already_in_incident");
        Assert.Equal(3, await InScope((_, db) => db.IncidentAssets.CountAsync()));
        Assert.Equal(3, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, first.Id))).Revision);
    }

    [Fact]
    public async Task RepeatedUnidentifiedNamesAreNotTreatedAsSamePhysicalAsset()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var a = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var b = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(2)));
        Assert.NotEqual(a.Asset.Id, b.Asset.Id);
        Assert.Equal(2, await InScope((_, db) => db.IncidentAssets.CountAsync()));
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task IncidentAssetWritersCanReportAttachAndLinkWithoutInventoryWritePermission(OrganizationRole role)
    {
        var seed = await Seed();
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var a = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("A")));
        var b = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("B")));
        var report = await Incidents(s => s.ReportAssetAsync(seed.Platform, seed.Org.Id, incident.Id, Reported()));
        await Incidents(s => s.AttachAssetAsync(seed.Platform, seed.Org.Id, incident.Id, new(a.Id, null, "Adjuntar", 2)));
        await Incidents(s => s.LinkAssetAsync(seed.Platform, seed.Org.Id, incident.Id, report.Asset.Id, new(b.Id, "Vincular", 3)));
        Assert.Equal(2, (await Incidents(s => s.AssetsAsync(seed.Platform, seed.Org.Id, incident.Id))).Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerOrPlatformWithoutMembershipCannotWriteIncidentAssets(bool viewer)
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        if (viewer)
        {
            await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.Viewer)));
            Assert.Single((await Incidents(s => s.AssetsAsync(seed.Platform, seed.Org.Id, incident.Id))).Items);
        }
        else await Rejected(403, () => Incidents(s => s.AssetsAsync(seed.Platform, seed.Org.Id, incident.Id)));
        await Rejected(403, () => Incidents(s => s.ReportAssetAsync(seed.Platform, seed.Org.Id, incident.Id, Reported(2))));
        await Rejected(403, () => Incidents(s => s.AttachAssetAsync(seed.Platform, seed.Org.Id, incident.Id, new(asset.Id, null, "Denied", 2))));
        await Rejected(403, () => Incidents(s => s.LinkAssetAsync(seed.Platform, seed.Org.Id, incident.Id, report.Asset.Id, new(asset.Id, "Denied", 2))));
        Assert.Equal(2, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Fact]
    public async Task IncidentAssetServicesAndCompositeForeignKeysRejectTenantMixing()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var foreignAsset = await Inventory(s => s.CreateAssetAsync(betaAdmin, beta.Id, SampleAsset()));
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        await Rejected(404, () => Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(foreignAsset.Id, null, "Denied", 2))));
        await Rejected(404, () => Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(foreignAsset.Id, "Denied", 2))));
        await Rejected(404, () => Incidents(s => s.AssetsAsync(betaAdmin, beta.Id, incident.Id)));
        await Rejected(403, () => Incidents(s => s.ReportAssetAsync(betaAdmin, seed.Org.Id, incident.Id, Reported(2))));
        foreach (var wrongIncident in new[] { true, false })
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
            {
                db.IncidentAssets.Add(new() { OrganizationId = wrongIncident ? beta.Id : seed.Org.Id, IncidentId = incident.Id,
                    AssetId = wrongIncident ? null : foreignAsset.Id, LinkedAtRevision = wrongIncident ? null : 2,
                    ReportedAtRevision = 2, ReportedName = "Foreign reference", ReportedType = AssetKind.Server });
                return await db.SaveChangesAsync();
            }));
            Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task AssociationIdCannotBeReusedUnderAnotherIncidentOfSameTenant()
    {
        var seed = await Seed();
        var first = await RegisterIncident(seed.Admin, seed.Org.Id);
        var second = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, first.Id, Reported()));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        await Rejected(404, () => Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, second.Id, report.Asset.Id, new(asset.Id, "Wrong incident", 1))));
        Assert.Empty((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, second.Id))).Items);
    }

    [Theory]
    [InlineData("UPDATE app.incident_assets SET reported_name='Tampered'")]
    [InlineData("UPDATE app.incident_assets SET reported_type='SERVER'")]
    [InlineData("UPDATE app.incident_assets SET reported_context='Tampered'")]
    [InlineData("UPDATE app.incident_assets SET reported_criticality='HIGH'")]
    [InlineData("UPDATE app.incident_assets SET reported_at_revision=99")]
    [InlineData("DELETE FROM app.incident_assets")]
    [InlineData("TRUNCATE app.incident_assets")]
    public async Task DatabaseProtectsOriginalReportEvenForObjectOwner(string sql)
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("42501", error.SqlState);
        Assert.Equal("Estación reportada", await InScope((_, db) => db.IncidentAssets.Select(a => a.ReportedName).SingleAsync()));
    }

    [Fact]
    public async Task InvalidAssetReportDoesNotAdvanceIncidentOrInsertHistory()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        foreach (var invalid in new[] { Reported() with { ReportedName = " " }, Reported() with { ReportedName = new string('x',201) },
            Reported() with { ReportedType = (AssetKind)999 }, Reported() with { ReportedCriticality = (AssetCriticality)999 },
            Reported() with { ReportedContext = new string('x',2001) }, Reported() with { Reason = " " } })
            await Rejected(400, () => Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, invalid)));
        await Rejected(409, () => Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(0))));
        Assert.Equal(0, await InScope((_, db) => db.IncidentAssets.CountAsync()));
        Assert.Equal(1, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(1, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
    }

    [Fact]
    public async Task ConcurrentLinksPreserveOriginalSnapshotAndCommitOneRevision()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var report = await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var a = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("A")));
        var b = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset("B")));
        async Task<int> Link(Guid asset)
        {
            try { await Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report.Asset.Id, new(asset, "Vincular", 2))); return 200; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        Assert.Equal(new[] {200,409}, (await Race(() => Link(a.Id), () => Link(b.Id))).Order().ToArray());
        var stored = Assert.Single((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items);
        Assert.Contains(stored.AssetId!.Value, new[] {a.Id,b.Id});
        Assert.Null(stored.ReportedCriticality);
        Assert.Equal(3, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentRetirementAndAttachmentFollowInventoryTransactionGate()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        async Task<int> Attach()
        {
            try { await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(asset.Id, null, "Adjuntar", 1))); return 200; }
            catch (IdentityOperationException e) { return e.StatusCode; }
        }
        async Task<int> Retire() { await Inventory(s => s.RetireAssetAsync(seed.Admin, seed.Org.Id, asset.Id, 1)); return 200; }
        var results = await Race(Attach, Retire);
        Assert.Contains(results[0], new[] {200,409}); Assert.Equal(200, results[1]);
        Assert.Equal(results[0] == 200 ? 1 : 0, await InScope((_, db) => db.IncidentAssets.CountAsync()));
        Assert.Equal(AssetStatus.Retired, (await Inventory(s => s.GetAssetAsync(seed.Admin, seed.Org.Id, asset.Id))).Status);
    }

    [Theory]
    [InlineData("report",false)]
    [InlineData("attach",false)]
    [InlineData("link",false)]
    [InlineData("report",true)]
    [InlineData("attach",true)]
    [InlineData("link",true)]
    public async Task FailureOfEitherHistoryRollsBackIncidentAssetAndAggregateRevision(string operation, bool timeline)
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        IncidentAssetChange? report = operation == "link" ? await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported())) : null;
        var revision = operation == "link" ? 2 : 1;
        var successBefore = await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "SUCCESS"));
        await InScope(async (_, db) =>
        {
            var sql = timeline ? """
                CREATE FUNCTION app.reject_asset_timeline_test() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected timeline outage'; END; $body$;
                CREATE TRIGGER reject_asset_timeline_test BEFORE INSERT ON app.incident_events FOR EACH ROW
                WHEN (NEW.action LIKE 'ASSET_%') EXECUTE FUNCTION app.reject_asset_timeline_test();
                """ : """
                CREATE FUNCTION audit.reject_asset_audit_test() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected audit outage'; END; $body$;
                CREATE TRIGGER reject_asset_audit_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
                WHEN (NEW.outcome='SUCCESS' AND NEW.action LIKE 'incident.asset_%') EXECUTE FUNCTION audit.reject_asset_audit_test();
                """;
            return await db.Database.ExecuteSqlRawAsync(sql);
        });
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (operation == "report") await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(revision)));
            else if (operation == "attach") await Incidents(s => s.AttachAssetAsync(seed.Admin, seed.Org.Id, incident.Id, new(asset.Id,null,"Prueba",revision)));
            else await Incidents(s => s.LinkAssetAsync(seed.Admin, seed.Org.Id, incident.Id, report!.Asset.Id, new(asset.Id,"Prueba",revision)));
        });
        Assert.Equal(revision, (await Incidents(s => s.GetAsync(seed.Admin, seed.Org.Id, incident.Id))).Revision);
        Assert.Equal(revision, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        Assert.Equal(operation == "link" ? 1 : 0, await InScope((_, db) => db.IncidentAssets.CountAsync()));
        if (operation == "link") Assert.Null(await InScope((_, db) => db.IncidentAssets.Select(a => a.AssetId).SingleAsync()));
        Assert.Equal(successBefore, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "SUCCESS")));
    }

    [Fact]
    public async Task AssetMigrationPreservesExistingIncidentHistoryWhenAddingDetails()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var before = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Single();
        await InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261022192153_InitialIncidentRegister");
            await db.Database.MigrateAsync();
            return true;
        });
        var after = (await Incidents(s => s.EventsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items.Single();
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.ActorUserId, after.ActorUserId);
        Assert.Equal(before.Title, after.Title); Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.OccurredAt, after.OccurredAt); Assert.Equal(before.CorrelationId, after.CorrelationId);
        Assert.Equal("{}", after.Details.GetRawText());
    }

    [Fact]
    public async Task AssetMigrationRefusesRollbackThatWouldLoseReportedHistory()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261022192153_InitialIncidentRegister");
            return true;
        }));
        Assert.Contains("Cannot roll back incident assets", error.MessageText);
        Assert.Single((await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id))).Items);
        Assert.Equal(2, await InScope((_, db) => db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task HttpAssetReportsRequireExplicitUnknownAndPreserveSnapshotAfterLink()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        var route = $"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/assets";
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        using var admin = Client(); await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route+"/reported",new {reportedName="Missing",reportedType="Server",reason="Test",expectedRevision=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(route+"/reported",new {reportedName="Missing",reportedCriticality=(string?)null,reason="Test",expectedRevision=1})).StatusCode);
        var response = await admin.PostAsJsonAsync(route+"/reported",new {reportedName="Reported HTTP",reportedType="Workstation",reportedCriticality=(string?)null,
            reportedContext="HTTP original",reason="Report",expectedRevision=1,assetId=asset.Id,organizationId=Guid.NewGuid()});
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<IncidentAssetChange>(InventoryJson))!;
        Assert.Null(report.Asset.AssetId);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"{route}/{report.Asset.Id}/link",new {assetId=asset.Id,reason="Missing revision"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync($"{route}/{report.Asset.Id}/link",new LinkIncidentAsset(asset.Id,"Link HTTP",2))).StatusCode);
        var listed = (await admin.GetFromJsonAsync<IncidentPage<IncidentAssetItem>>(route,InventoryJson))!;
        Assert.Equal("Reported HTTP",Assert.Single(listed.Items).ReportedName);
        Assert.Null(listed.Items[0].ReportedCriticality);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await admin.DeleteAsync($"{route}/{report.Asset.Id}/link")).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route+"/reported",Reported(3))).StatusCode);
    }

    [Fact]
    public async Task IncidentAssetListsUseBoundedPagination()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported()));
        await Incidents(s => s.ReportAssetAsync(seed.Admin, seed.Org.Id, incident.Id, Reported(2)));
        var first = await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id,limit:1));
        var second = await Incidents(s => s.AssetsAsync(seed.Admin, seed.Org.Id, incident.Id,skip:first.NextSkip!.Value,limit:1));
        Assert.NotEqual(Assert.Single(first.Items).Id,Assert.Single(second.Items).Id); Assert.Null(second.NextSkip);
        await Rejected(400,() => Incidents(s => s.AssetsAsync(seed.Admin,seed.Org.Id,incident.Id,limit:101)));
        await Rejected(400,() => Incidents(s => s.AssetsAsync(seed.Admin,seed.Org.Id,incident.Id,skip:-1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncidentEventDetailsRequireBoundedJsonObject(bool oversized)
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_,db) =>
        {
            db.IncidentEvents.Add(new() {OrganizationId=seed.Org.Id,IncidentId=incident.Id,Revision=2,ActorUserId=seed.Admin,
                Action="UPDATED",Reason="SQL check",CategoryId=incident.CategoryId,Title="Test",DetailsJson=oversized ? JsonSerializer.Serialize(new {value=new string('x',32769)}) : "[]"});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
