using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private static readonly JsonSerializerOptions InventoryJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public async Task HttpCatalogAndProfileRespectRolesRevisionsAndStringEnums()
    {
        var seed = await Seed();
        using var platform = Client();
        await Login(platform, "platform");
        var createdResponse = await platform.PostAsJsonAsync("/api/catalogs/capabilities", new CatalogCreate("HTTP_EXTENSION", "Extensión HTTP", "CUSTOM", null));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CatalogItem>();
        Assert.NotNull(created);
        Assert.Equal(HttpStatusCode.OK, (await platform.PutAsJsonAsync($"/api/catalogs/capabilities/{created.Id}", new CatalogUpdate("Editado", "CUSTOM", null, false, 1))).StatusCode);
        using var admin = Client();
        await Login(admin, "alpha");
        var capability = await CapabilityId();
        var setResponse = await admin.PutAsJsonAsync($"/api/organizations/{seed.Org.Id}/capabilities/{capability}", new { availability = "Available", notes = "Nota HTTP", expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var profile = await admin.GetFromJsonAsync<InventoryPage<CapabilityItem>>($"/api/organizations/{seed.Org.Id}/capabilities", InventoryJson);
        Assert.Equal(CapabilityAvailability.Available, profile!.Items.Single(c => c.CapabilityId == capability).Availability);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/organizations/{seed.Org.Id}/capabilities/{capability}", new { availability = "Unknown", expectedRevision = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/organizations/{seed.Org.Id}/capabilities")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/catalogs/unknown")).StatusCode);
    }

    [Fact]
    public async Task DeactivatedAccountCannotReadOrWriteInventory()
    {
        var seed = await Seed();
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        await Act(async s => { await s.SetUserActiveAsync(seed.Platform, seed.Admin, false); return true; });
        await Rejected(403, () => Inventory(s => s.GetCapabilitiesAsync(seed.Admin, seed.Org.Id)));
        await Rejected(403, () => Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset())));
    }

    [Fact]
    public async Task RevokedPlatformGrantCannotModifyCatalog()
    {
        var seed = await Seed();
        await InScope(async (_, db) => { (await db.PlatformAdministrators.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return true; });
        await Rejected(403, () => Inventory(s => s.CreateCatalogAsync(seed.Platform, CatalogKind.Capabilities, new("REVOKED_TEST", "Denied", "CUSTOM", null))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogAndDeclarationUniquenessAreEnforcedInDatabase(bool duplicateCatalog)
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            if (duplicateCatalog)
                db.CapabilityCatalog.Add(new() { Code = "MFA", Name = "Duplicate", Category = "CUSTOM" });
            else
            {
                db.OrganizationCapabilities.Add(new() { OrganizationId = seed.Org.Id, CapabilityId = capability });
                db.OrganizationCapabilities.Add(new() { OrganizationId = seed.Org.Id, CapabilityId = capability });
            }
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23505", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
    [Fact]
    public async Task HttpRequiresExplicitAssetClassificationAndWritePreconditions()
    {
        var seed = await Seed();
        using var admin = Client();
        await Login(admin, "alpha");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/organizations/{seed.Org.Id}/assets", new { name = "Missing classification" })).StatusCode);
        var capability = await CapabilityId();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/organizations/{seed.Org.Id}/capabilities/{capability}", new { availability = "Available" })).StatusCode);
        Assert.Equal(0, await InScope((_, db) => db.Assets.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
    }

    [Fact]
    public async Task CatalogCodeCannotIncludeTrailingNewline()
    {
        var seed = await Seed();
        await Rejected(400, () => Inventory(s => s.CreateCatalogAsync(seed.Platform, CatalogKind.Capabilities,
            new("VALID_BUT_NEWLINE\n", "Invalid", "CUSTOM", null))), "invalid_catalog_code");
    }
    private async Task<T> Inventory<T>(Func<IInventoryService, Task<T>> action)
    {
        using var scope = app.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInventoryService>());
    }
    private static AssetCreate SampleAsset(string name = "Servidor de prueba") => new(name, AssetKind.Server, AssetCriticality.High, "Descripción privada");
    private async Task<Guid> CapabilityId(string code = "BACKUPS") => await InScope((_, db) => db.CapabilityCatalog.Where(c => c.Code == code).Select(c => c.Id).SingleAsync());

    [Fact]
    public async Task ReferenceCatalogsContainAgreedSeedsWithoutBusinessIdentities()
    {
        Assert.Equal(8, await InScope((_, db) => db.CapabilityCatalog.CountAsync()));
        Assert.Equal(3, await InScope((_, db) => db.IncidentCategories.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.Users.CountAsync()));
        var seed = await Seed();
        var catalog = await Inventory(s => s.ListCatalogAsync(seed.Platform, CatalogKind.Capabilities));
        Assert.Contains(catalog.Items, c => c.Code == "DEVICE_ISOLATION");
        Assert.Contains(catalog.Items, c => c.Code == "MFA");
    }

    [Fact]
    public async Task UndeclaredCapabilitiesAreUnknownWithoutCreatingRows()
    {
        var seed = await Seed();
        var profile = await Inventory(s => s.GetCapabilitiesAsync(seed.Admin, seed.Org.Id));
        Assert.Equal(8, profile.Items.Count);
        Assert.All(profile.Items, c => { Assert.Equal(CapabilityAvailability.Unknown, c.Availability); Assert.Equal(0, c.Revision); Assert.Null(c.Notes); });
        Assert.Equal(0, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
        Assert.Equal(2, await InScope((_, db) => db.AuditEvents.CountAsync()));
    }

    [Fact]
    public async Task OrganizationDeclarationsDoNotChangeAnotherOrganizationProfile()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var id = await CapabilityId();
        var declared = await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, id, new(CapabilityAvailability.Available, "Nota privada A", 0)));
        Assert.Equal(1, declared.Revision);
        var other = await Inventory(s => s.GetCapabilitiesAsync(betaAdmin, beta.Id));
        Assert.Equal(CapabilityAvailability.Unknown, other.Items.Single(c => c.CapabilityId == id).Availability);
        await Rejected(403, () => Inventory(s => s.PutCapabilityAsync(seed.Admin, beta.Id, id, new(CapabilityAvailability.Unavailable, null, 0))));
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ReadOnlyInventoryRolesCannotWriteProfileAssetsOrGlobalCatalog(OrganizationRole role)
    {
        var seed = await Seed();
        await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, role)));
        await Inventory(s => s.GetCapabilitiesAsync(seed.Platform, seed.Org.Id));
        Assert.Empty((await Inventory(s => s.ListAssetsAsync(seed.Platform, seed.Org.Id))).Items);
        var capability = await CapabilityId();
        await Rejected(403, () => Inventory(s => s.PutCapabilityAsync(seed.Platform, seed.Org.Id, capability, new(CapabilityAvailability.Available, null, 0))));
        await Rejected(403, () => Inventory(s => s.CreateAssetAsync(seed.Platform, seed.Org.Id, SampleAsset())));
        await Rejected(403, () => Inventory(s => s.CreateCatalogAsync(seed.Admin, CatalogKind.Capabilities, new("CUSTOM_TEST", "Custom", "CUSTOM", null))));
    }

    [Fact]
    public async Task PlatformRoleHasNoImplicitInventoryTenantAccess()
    {
        var seed = await Seed();
        await Rejected(403, () => Inventory(s => s.GetCapabilitiesAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => Inventory(s => s.ListAssetsAsync(seed.Platform, seed.Org.Id)));
        await Rejected(403, () => Inventory(s => s.CreateAssetAsync(seed.Platform, seed.Org.Id, SampleAsset())));
    }

    [Theory]
    [InlineData(CatalogKind.Capabilities)]
    [InlineData(CatalogKind.IncidentCategories)]
    public async Task PlatformCanExtendAndDeactivateCatalogWithRevisionProtection(CatalogKind kind)
    {
        var seed = await Seed();
        var entry = await Inventory(s => s.CreateCatalogAsync(seed.Platform, kind, new("EXTENDED", "Extensión", "CUSTOM", "Privado")));
        var updated = await Inventory(s => s.UpdateCatalogAsync(seed.Platform, kind, entry.Id, new("Nombre cambiado", "CUSTOM", null, false, entry.Revision)));
        Assert.Equal("EXTENDED", updated.Code);
        Assert.Equal(2, updated.Revision);
        Assert.DoesNotContain((await Inventory(s => s.ListCatalogAsync(seed.Admin, kind))).Items, c => c.Id == entry.Id);
        Assert.Contains((await Inventory(s => s.ListCatalogAsync(seed.Admin, kind, includeInactive: true))).Items, c => c.Id == entry.Id);
        await Rejected(409, () => Inventory(s => s.UpdateCatalogAsync(seed.Platform, kind, entry.Id, new("Stale", "CUSTOM", null, true, 1))));
        await Rejected(409, () => Inventory(s => s.CreateCatalogAsync(seed.Platform, kind, new("EXTENDED", "Duplicate", "CUSTOM", null))));
    }

    [Fact]
    public async Task InactiveCapabilityKeepsExistingDeclarationButRejectsFurtherWrites()
    {
        var seed = await Seed();
        var capability = (await Inventory(s => s.ListCatalogAsync(seed.Platform, CatalogKind.Capabilities))).Items.Single(c => c.Code == "BACKUPS");
        await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability.Id, new(CapabilityAvailability.Available, "Mantener", 0)));
        await Inventory(s => s.UpdateCatalogAsync(seed.Platform, CatalogKind.Capabilities, capability.Id, new(capability.Name, capability.Category, capability.Description, false, 1)));
        var current = (await Inventory(s => s.GetCapabilitiesAsync(seed.Admin, seed.Org.Id))).Items.Single(c => c.CapabilityId == capability.Id);
        Assert.False(current.IsActive);
        Assert.Equal(CapabilityAvailability.Available, current.Availability);
        await Rejected(409, () => Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability.Id, new(CapabilityAvailability.Unavailable, null, 1))), "inactive_capability");
    }

    [Fact]
    public async Task ResetToUnknownRetainsVersionAndPreventsAbsentVersionOverwrite()
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Available, "Anterior", 0)));
        var reset = await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Unknown, null, 1)));
        Assert.Equal(2, reset.Revision);
        Assert.Equal(CapabilityAvailability.Unknown, reset.Availability);
        await Rejected(409, () => Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Unavailable, "Stale", 0))));
        Assert.Equal(1, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentCapabilityDeclarationsCreateOnlyOneRow()
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        async Task<int> Attempt(CapabilityAvailability availability)
        { try { await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(availability, null, 0))); return 200; } catch (OpenIRP.Application.Identity.IdentityOperationException e) { return e.StatusCode; } }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Attempt(CapabilityAvailability.Available), () => Attempt(CapabilityAvailability.Unavailable))).Order().ToArray());
        Assert.Equal(1, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
    }

    [Fact]
    public async Task AssetUpdatesRetirementAndReactivationPreserveIdentity()
    {
        var seed = await Seed();
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        var updated = await Inventory(s => s.UpdateAssetAsync(seed.Admin, seed.Org.Id, asset.Id, new("Renombrado", AssetKind.Application, AssetCriticality.Low, null, AssetStatus.Active, 1)));
        Assert.Equal(2, updated.Revision);
        var retired = await Inventory(s => s.RetireAssetAsync(seed.Admin, seed.Org.Id, asset.Id, 2));
        Assert.Equal(AssetStatus.Retired, retired.Status);
        Assert.Equal(3, retired.Revision);
        Assert.Equal(asset.Id, (await Inventory(s => s.GetAssetAsync(seed.Admin, seed.Org.Id, asset.Id))).Id);
        var restored = await Inventory(s => s.UpdateAssetAsync(seed.Admin, seed.Org.Id, asset.Id, new("Reactivado", AssetKind.Application, AssetCriticality.Low, null, AssetStatus.Active, 3)));
        Assert.Equal(4, restored.Revision);
        Assert.Equal(1, await InScope((_, db) => db.Assets.CountAsync()));
    }

    [Fact]
    public async Task AssetsAreScopedInLookupsListsAndMutations()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var betaAdmin = await InScope((_, db) => db.OrganizationMemberships.Where(m => m.OrganizationId == beta.Id).Select(m => m.UserId).SingleAsync());
        var asset = await Inventory(s => s.CreateAssetAsync(betaAdmin, beta.Id, SampleAsset("Solo Beta")));
        Assert.Empty((await Inventory(s => s.ListAssetsAsync(seed.Admin, seed.Org.Id))).Items);
        await Rejected(404, () => Inventory(s => s.GetAssetAsync(seed.Admin, seed.Org.Id, asset.Id)));
        await Rejected(404, () => Inventory(s => s.RetireAssetAsync(seed.Admin, seed.Org.Id, asset.Id, 1)));
        await Rejected(403, () => Inventory(s => s.GetAssetAsync(seed.Admin, beta.Id, asset.Id)));
        Assert.Equal(AssetStatus.Active, (await Inventory(s => s.GetAssetAsync(betaAdmin, beta.Id, asset.Id))).Status);
    }

    [Fact]
    public async Task ConcurrentAssetUpdatesRejectLostUpdate()
    {
        var seed = await Seed();
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        async Task<int> Attempt(string name)
        { try { await Inventory(s => s.UpdateAssetAsync(seed.Admin, seed.Org.Id, asset.Id, new(name, AssetKind.Server, AssetCriticality.High, null, AssetStatus.Active, 1))); return 200; } catch (OpenIRP.Application.Identity.IdentityOperationException e) { return e.StatusCode; } }
        Assert.Equal(new[] { 200, 409 }, (await Race(() => Attempt("A"), () => Attempt("B"))).Order().ToArray());
        Assert.Equal(2, (await Inventory(s => s.GetAssetAsync(seed.Admin, seed.Org.Id, asset.Id))).Revision);
    }

    [Fact]
    public async Task InvalidInputAndMissingReferencesAreRejectedWithoutRows()
    {
        var seed = await Seed();
        await Rejected(400, () => Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, new(" ", AssetKind.Server, AssetCriticality.Low, null))));
        await Rejected(400, () => Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, new("Bad", (AssetKind)999, AssetCriticality.Low, null))));
        await Rejected(400, () => Inventory(s => s.CreateCatalogAsync(seed.Platform, CatalogKind.Capabilities, new("lowercase", "Bad", "CUSTOM", null))));
        await Rejected(404, () => Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, Guid.NewGuid(), new(CapabilityAvailability.Available, null, 0))));
        var capability = await CapabilityId();
        await Rejected(400, () => Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new((CapabilityAvailability)999, null, 0))));
        Assert.Equal(0, await InScope((_, db) => db.Assets.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
    }

    [Fact]
    public async Task DatabaseChecksAndForeignKeysRejectBypassWrites()
    {
        var seed = await Seed();
        var check = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.Assets.Add(new Asset { OrganizationId = seed.Org.Id, Name = "Direct SQL equivalent", Kind = (AssetKind)999 });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514", Assert.IsType<PostgresException>(check.InnerException).SqlState);
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => InScope(async (_, db) =>
        {
            db.OrganizationCapabilities.Add(new() { OrganizationId = seed.Org.Id, CapabilityId = Guid.NewGuid() });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503", Assert.IsType<PostgresException>(foreign.InnerException).SqlState);
    }

    [Fact]
    public async Task CompositeAssetKeyRejectsCrossOrganizationAssociationInPostgres()
    {
        var seed = await Seed();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var asset = await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset()));
        await InScope(async (_, db) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE asset_tenant_probe (organization_id uuid NOT NULL, asset_id uuid NOT NULL, FOREIGN KEY (organization_id, asset_id) REFERENCES app.assets(organization_id, id))");
            await tx.CreateSavepointAsync("probe");
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO asset_tenant_probe VALUES ({beta.Id}, {asset.Id})"));
            Assert.Equal("23503", error.SqlState);
            await tx.RollbackToSavepointAsync("probe");
            Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO asset_tenant_probe VALUES ({seed.Org.Id}, {asset.Id})"));
            await tx.RollbackAsync();
            return true;
        });
    }

    [Fact]
    public async Task CatalogReferenceCannotBeDeletedWhileOrganizationDeclaresIt()
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Available, null, 0)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => InScope((_, db) => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.capability_catalog WHERE id={capability}")));
        Assert.Contains(error.SqlState, new[] { "23503", "23001" });
    }

    [Fact]
    public async Task AuditFailureRollsBackAllNewInventoryWriteTypes()
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        await InScope(async (_, db) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION audit.reject_inventory_test() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Injected inventory audit outage'; END; $body$;
                CREATE TRIGGER reject_inventory_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
                WHEN (NEW.outcome='SUCCESS' AND NEW.action IN ('asset.create','capability.set','catalog.create'))
                EXECUTE FUNCTION audit.reject_inventory_test();
                """);
            return true;
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, SampleAsset())));
        await Assert.ThrowsAsync<DbUpdateException>(() => Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Available, null, 0))));
        await Assert.ThrowsAsync<DbUpdateException>(() => Inventory(s => s.CreateCatalogAsync(seed.Platform, CatalogKind.Capabilities, new("ROLLBACK_TEST", "Rollback", "CUSTOM", null))));
        Assert.Equal(0, await InScope((_, db) => db.Assets.CountAsync()));
        Assert.Equal(0, await InScope((_, db) => db.OrganizationCapabilities.CountAsync()));
        Assert.Equal(8, await InScope((_, db) => db.CapabilityCatalog.CountAsync()));
        Assert.Equal(3, await InScope((_, db) => db.AuditEvents.CountAsync(e => e.Outcome == "FAILURE")));
    }

    [Fact]
    public async Task AuditDoesNotCopyFreeTextNamesDescriptionsOrNotes()
    {
        var seed = await Seed();
        var capability = await CapabilityId();
        await Inventory(s => s.CreateAssetAsync(seed.Admin, seed.Org.Id, new("private-hostname.example.test", AssetKind.Server, AssetCriticality.High, "private-description-123")));
        await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Available, "private-capability-note-456", 0)));
        var json = JsonSerializer.Serialize(await InScope((_, db) => db.AuditEvents.ToListAsync()));
        Assert.DoesNotContain("private-hostname", json);
        Assert.DoesNotContain("private-description", json);
        Assert.DoesNotContain("private-capability-note", json);
        Assert.Contains("asset.create", json);
        Assert.Contains("capability.set", json);
    }

    [Fact]
    public async Task InventoryPaginationIsBoundedAndDeterministicWithoutConcurrentWrites()
    {
        var seed = await Seed();
        var first = await Inventory(s => s.GetCapabilitiesAsync(seed.Admin, seed.Org.Id, limit: 3));
        var second = await Inventory(s => s.GetCapabilitiesAsync(seed.Admin, seed.Org.Id, skip: first.NextSkip!.Value, limit: 3));
        Assert.Equal(3, first.Items.Count);
        Assert.Empty(first.Items.Select(c => c.CapabilityId).Intersect(second.Items.Select(c => c.CapabilityId)));
        await Rejected(400, () => Inventory(s => s.ListAssetsAsync(seed.Admin, seed.Org.Id, limit: 101)));
        await Rejected(400, () => Inventory(s => s.ListCatalogAsync(seed.Platform, CatalogKind.Capabilities, skip: -1)));
    }

    [Fact]
    public async Task SuspendedMembershipOrOrganizationCannotUseNewModule()
    {
        var seed = await Seed();
        var member = await Act(s => s.AddMemberAsync(seed.Admin, seed.Org.Id, new(seed.Platform, OrganizationRole.OrgAdmin)));
        await Act(s => s.ChangeMemberAsync(seed.Admin, seed.Org.Id, member.Id, new(OrganizationRole.OrgAdmin, MembershipStatus.Suspended, null)));
        await Rejected(403, () => Inventory(s => s.GetCapabilitiesAsync(seed.Platform, seed.Org.Id)));
        await InScope(async (_, db) => { (await db.Organizations.SingleAsync()).Status = OrganizationStatus.Suspended; await db.SaveChangesAsync(); return true; });
        await Rejected(403, () => Inventory(s => s.ListAssetsAsync(seed.Admin, seed.Org.Id)));
    }

    [Fact]
    public async Task InventoryMigrationRoundTripsWithoutRewritingEarlierMigrations()
    {
        await InScope(async (_, db) =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261022175420_BasicAdministrativeAudit");
            await db.Database.MigrateAsync();
            Assert.Equal(8, await db.CapabilityCatalog.CountAsync());
            Assert.Equal(3, await db.IncidentCategories.CountAsync());
            Assert.Equal(0, await db.Assets.CountAsync());
            return true;
        });
    }

    [Fact]
    public async Task HttpInventoryUsesCsrfVersionsAndSoftRetirement()
    {
        var seed = await Seed();
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/catalogs/capabilities")).StatusCode);
        using var admin = Client();
        await Login(admin, "alpha");
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/organizations/{seed.Org.Id}/assets", SampleAsset())).StatusCode);
        await Csrf(admin);
        var response = await admin.PostAsJsonAsync($"/api/organizations/{seed.Org.Id}/assets", SampleAsset());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AssetItem>(InventoryJson);
        Assert.NotNull(created);
        var retired = await admin.DeleteAsync($"/api/organizations/{seed.Org.Id}/assets/{created.Id}?expectedRevision=1");
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var readBack = await admin.GetFromJsonAsync<AssetItem>($"/api/organizations/{seed.Org.Id}/assets/{created.Id}", InventoryJson);
        Assert.Equal(AssetStatus.Retired, readBack!.Status);
        Assert.Equal(2, readBack.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/organizations/{seed.Org.Id}/assets/{created.Id}?expectedRevision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/catalogs/capabilities", new CatalogCreate("DENIED", "Denied", "CUSTOM", null))).StatusCode);
    }
}
