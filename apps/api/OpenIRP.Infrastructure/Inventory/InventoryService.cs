using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Audit;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Inventory;

public sealed class InventoryService(OpenIrpDbContext db, AuditRecorder audit) : IInventoryService
{
    private sealed record Mutation<T>(T Value, Guid ResourceId, string Details = "{}");
    private static IdentityOperationException Error(int status, string code) => new(status, code);

    // Same database gate as Identity: permissions and account deactivation cannot race these writes.
    private async Task<T> WriteAsync<T>(AuditAttempt attempt, Func<Task<Mutation<T>>> operation, CancellationToken ct)
    {
        try
        {
            if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean inventory scope.");
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)", ct);
            var result = await operation();
            db.AuditEvents.Add(audit.Success(attempt with { ResourceId = result.ResourceId }, result.Details));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result.Value;
        }
        catch (Exception e)
        {
            Exception effective = e switch
            {
                DbUpdateConcurrencyException => Error(409, "concurrent_inventory_change"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => Error(409, "duplicate_catalog_or_capability"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23503" or "23514" } } => Error(400, "invalid_inventory_reference_or_value"),
                PostgresException { SqlState: "55P03" or "40P01" or "40001" } => Error(409, "concurrent_change_retry"),
                DbUpdateException { InnerException: PostgresException { SqlState: "55P03" or "40P01" or "40001" } } => Error(409, "concurrent_change_retry"),
                _ => e
            };
            await audit.RejectedAsync(attempt, effective);
            if (ReferenceEquals(e, effective)) throw;
            throw effective;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<T> ReadAsync<T>(AuditAttempt attempt, Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (IdentityOperationException e) { await audit.RejectedAsync(attempt, e); throw; }
    }

    private async Task ActiveAsync(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct)) throw Error(403, "access_denied");
    }

    private async Task PlatformAsync(Guid actor, CancellationToken ct)
    {
        await ActiveAsync(actor, ct);
        if (!await db.PlatformAdministrators.AnyAsync(p => p.UserId == actor && p.RevokedAt == null, ct)) throw Error(403, "access_denied");
    }

    private async Task OrganizationAsync(Guid actor, Guid organization, bool write, CancellationToken ct)
    {
        await ActiveAsync(actor, ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == organization && o.Status == OrganizationStatus.Active, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Status == MembershipStatus.Active && (!write || m.Role == OrganizationRole.OrgAdmin), ct))
            throw Error(403, "access_denied");
    }

    private static string CatalogName(CatalogKind kind) => kind switch
    {
        CatalogKind.Capabilities => "capability_catalog",
        CatalogKind.IncidentCategories => "incident_category",
        _ => throw Error(400, "invalid_catalog")
    };

    private IQueryable<CatalogItem> CatalogQuery(CatalogKind kind, bool includeInactive) => kind switch
    {
        CatalogKind.Capabilities => db.CapabilityCatalog.AsNoTracking().Where(e => includeInactive || e.IsActive).OrderBy(e => e.Code).Select(e => new CatalogItem(e.Id, e.Code, e.Name, e.Category, e.Description, e.IsActive, e.Revision)),
        CatalogKind.IncidentCategories => db.IncidentCategories.AsNoTracking().Where(e => includeInactive || e.IsActive).OrderBy(e => e.Code).Select(e => new CatalogItem(e.Id, e.Code, e.Name, e.Category, e.Description, e.IsActive, e.Revision)),
        _ => throw Error(400, "invalid_catalog")
    };

    private static void PageArguments(int skip, int limit)
    {
        if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_inventory_pagination");
    }

    private static async Task<InventoryPage<T>> PageAsync<T>(IQueryable<T> query, int skip, int limit, CancellationToken ct)
    {
        PageArguments(skip, limit);
        var rows = await query.Skip(skip).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows, more ? skip + limit : null);
    }

    public Task<InventoryPage<CatalogItem>> ListCatalogAsync(Guid actor, CatalogKind kind, bool includeInactive = false, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(new("catalog.list", actor, null, "catalog"), async () =>
        {
            await ActiveAsync(actor, ct);
            return await PageAsync(CatalogQuery(kind, includeInactive), skip, limit, ct);
        });

    public Task<CatalogItem> CreateCatalogAsync(Guid actor, CatalogKind kind, CatalogCreate request, CancellationToken ct = default) => kind switch
    {
        CatalogKind.Capabilities => CreateCatalogCoreAsync<CapabilityCatalogEntry>(actor, kind, request, ct),
        CatalogKind.IncidentCategories => CreateCatalogCoreAsync<IncidentCategory>(actor, kind, request, ct),
        _ => throw Error(400, "invalid_catalog")
    };

    private Task<CatalogItem> CreateCatalogCoreAsync<T>(Guid actor, CatalogKind kind, CatalogCreate request, CancellationToken ct) where T : class, ICatalogRecord, new() =>
        WriteAsync<CatalogItem>(new("catalog.create", actor, null, CatalogName(kind)), async () =>
        {
            await PlatformAsync(actor, ct);
            if (request.Code is null || request.Code.Length > 60 || !Regex.IsMatch(request.Code, @"\A[A-Z][A-Z0-9_]{1,59}\z")) throw Error(400, "invalid_catalog_code");
            CatalogFields(request.Name, request.Category, request.Description);
            var entity = new T { Id = Guid.NewGuid(), Code = request.Code, Name = request.Name.Trim(), Category = request.Category.Trim(), Description = request.Description, IsActive = true, Revision = 1 };
            db.Set<T>().Add(entity);
            return new(View(entity), entity.Id, "{\"revision\":1}");
        }, ct);

    public Task<CatalogItem> UpdateCatalogAsync(Guid actor, CatalogKind kind, Guid id, CatalogUpdate request, CancellationToken ct = default) => kind switch
    {
        CatalogKind.Capabilities => UpdateCatalogCoreAsync<CapabilityCatalogEntry>(actor, kind, id, request, ct),
        CatalogKind.IncidentCategories => UpdateCatalogCoreAsync<IncidentCategory>(actor, kind, id, request, ct),
        _ => throw Error(400, "invalid_catalog")
    };

    private Task<CatalogItem> UpdateCatalogCoreAsync<T>(Guid actor, CatalogKind kind, Guid id, CatalogUpdate request, CancellationToken ct) where T : class, ICatalogRecord =>
        WriteAsync<CatalogItem>(new("catalog.update", actor, null, CatalogName(kind), id), async () =>
        {
            await PlatformAsync(actor, ct);
            CatalogFields(request.Name, request.Category, request.Description);
            var entity = await db.Set<T>().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw Error(404, "catalog_item_not_found");
            entity.Revision = Next(entity.Revision, request.ExpectedRevision);
            entity.Name = request.Name.Trim(); entity.Category = request.Category.Trim();
            entity.Description = request.Description; entity.IsActive = request.IsActive;
            return new(View(entity), entity.Id, JsonSerializer.Serialize(new { entity.Revision, entity.IsActive }));
        }, ct);

    public Task<InventoryPage<CapabilityItem>> GetCapabilitiesAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(new("capability.list", actor, organization, "organization_capability"), async () =>
        {
            await OrganizationAsync(actor, organization, false, ct);
            var query = from catalog in db.CapabilityCatalog.AsNoTracking()
                        join declaration in db.OrganizationCapabilities.AsNoTracking().Where(e => e.OrganizationId == organization)
                            on catalog.Id equals declaration.CapabilityId into declarations
                        from declaration in declarations.DefaultIfEmpty()
                        where catalog.IsActive || declaration != null
                        orderby catalog.Code
                        select new CapabilityItem(catalog.Id, catalog.Code, catalog.Name, catalog.IsActive,
                            declaration == null ? CapabilityAvailability.Unknown : declaration.Availability,
                            declaration == null ? null : declaration.Notes, declaration == null ? 0 : declaration.Revision);
            return await PageAsync(query, skip, limit, ct);
        });

    public Task<CapabilityItem> PutCapabilityAsync(Guid actor, Guid organization, Guid capability, CapabilityPut request, CancellationToken ct = default) =>
        WriteAsync<CapabilityItem>(new("capability.set", actor, organization, "organization_capability"), async () =>
        {
            await OrganizationAsync(actor, organization, true, ct);
            if (!Enum.IsDefined(request.Availability) || request.Notes?.Length > 2000) throw Error(400, "invalid_capability");
            var catalog = await db.CapabilityCatalog.SingleOrDefaultAsync(e => e.Id == capability, ct) ?? throw Error(404, "capability_not_found");
            if (!catalog.IsActive) throw Error(409, "inactive_capability");
            var entity = await db.OrganizationCapabilities.SingleOrDefaultAsync(e => e.OrganizationId == organization && e.CapabilityId == capability, ct);
            if (entity is null)
            {
                if (request.ExpectedRevision != 0) throw Error(409, "stale_inventory_revision");
                entity = new OrganizationCapability { OrganizationId = organization, CapabilityId = capability };
                db.OrganizationCapabilities.Add(entity);
            }
            else entity.Revision = Next(entity.Revision, request.ExpectedRevision);
            entity.Availability = request.Availability; entity.Notes = request.Notes;
            return new(new(capability, catalog.Code, catalog.Name, catalog.IsActive, entity.Availability, entity.Notes, entity.Revision), entity.Id,
                JsonSerializer.Serialize(new { capabilityId = capability, entity.Revision, availability = entity.Availability.ToString() }));
        }, ct);

    private IQueryable<Asset> AssetQuery(Guid organization) => db.Assets.AsNoTracking().Where(a => a.OrganizationId == organization);

    public Task<InventoryPage<AssetItem>> ListAssetsAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(new("asset.list", actor, organization, "asset"), async () =>
        {
            await OrganizationAsync(actor, organization, false, ct);
            return await PageAsync(AssetQuery(organization).OrderBy(a => a.Id).Select(a => new AssetItem(a.Id, a.OrganizationId, a.Name, a.Kind, a.Criticality, a.Description, a.Status, a.Revision, a.CreatedAt)), skip, limit, ct);
        });

    public Task<AssetItem> GetAssetAsync(Guid actor, Guid organization, Guid asset, CancellationToken ct = default) =>
        ReadAsync(new("asset.read", actor, organization, "asset", asset), async () =>
        {
            await OrganizationAsync(actor, organization, false, ct);
            var entity = await AssetQuery(organization).SingleOrDefaultAsync(a => a.Id == asset, ct) ?? throw Error(404, "asset_not_found");
            return View(entity);
        });

    public Task<AssetItem> CreateAssetAsync(Guid actor, Guid organization, AssetCreate request, CancellationToken ct = default) =>
        WriteAsync<AssetItem>(new("asset.create", actor, organization, "asset"), async () =>
        {
            await OrganizationAsync(actor, organization, true, ct);
            AssetFields(request.Name, request.Kind, request.Criticality, request.Description);
            var entity = new Asset { OrganizationId = organization, Name = request.Name.Trim(), Kind = request.Kind, Criticality = request.Criticality, Description = request.Description };
            db.Assets.Add(entity);
            return new(View(entity), entity.Id, "{\"revision\":1}");
        }, ct);

    public Task<AssetItem> UpdateAssetAsync(Guid actor, Guid organization, Guid asset, AssetUpdate request, CancellationToken ct = default) =>
        WriteAsync<AssetItem>(new("asset.update", actor, organization, "asset", asset), async () =>
        {
            await OrganizationAsync(actor, organization, true, ct);
            AssetFields(request.Name, request.Kind, request.Criticality, request.Description);
            if (!Enum.IsDefined(request.Status)) throw Error(400, "invalid_asset_status");
            var entity = await db.Assets.SingleOrDefaultAsync(a => a.OrganizationId == organization && a.Id == asset, ct) ?? throw Error(404, "asset_not_found");
            entity.Revision = Next(entity.Revision, request.ExpectedRevision);
            entity.Name = request.Name.Trim(); entity.Kind = request.Kind; entity.Criticality = request.Criticality;
            entity.Description = request.Description; entity.Status = request.Status;
            return new(View(entity), entity.Id, JsonSerializer.Serialize(new { entity.Revision, status = entity.Status.ToString() }));
        }, ct);

    public Task<AssetItem> RetireAssetAsync(Guid actor, Guid organization, Guid asset, long expectedRevision, CancellationToken ct = default) =>
        WriteAsync<AssetItem>(new("asset.retire", actor, organization, "asset", asset), async () =>
        {
            await OrganizationAsync(actor, organization, true, ct);
            var entity = await db.Assets.SingleOrDefaultAsync(a => a.OrganizationId == organization && a.Id == asset, ct) ?? throw Error(404, "asset_not_found");
            entity.Revision = Next(entity.Revision, expectedRevision); entity.Status = AssetStatus.Retired;
            return new(View(entity), entity.Id, JsonSerializer.Serialize(new { entity.Revision, status = "Retired" }));
        }, ct);

    private static long Next(long current, long expected)
    {
        if (current != expected || current == long.MaxValue) throw Error(409, "stale_inventory_revision");
        return current + 1;
    }
    private static void CatalogFields(string name, string category, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(category) || category.Length > 60 || description?.Length > 2000)
            throw Error(400, "invalid_catalog_fields");
    }
    private static void AssetFields(string name, AssetKind kind, AssetCriticality criticality, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || !Enum.IsDefined(kind) || !Enum.IsDefined(criticality) || description?.Length > 2000)
            throw Error(400, "invalid_asset_fields");
    }
    private static CatalogItem View(ICatalogRecord e) => new(e.Id, e.Code, e.Name, e.Category, e.Description, e.IsActive, e.Revision);
    private static AssetItem View(Asset e) => new(e.Id, e.OrganizationId, e.Name, e.Kind, e.Criticality, e.Description, e.Status, e.Revision, e.CreatedAt);
}
