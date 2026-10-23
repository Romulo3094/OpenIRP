using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Inventory;

public enum CatalogKind { Capabilities, IncidentCategories }
public sealed record CatalogCreate(string Code, string Name, string Category, string? Description);
public sealed record CatalogUpdate(string Name, string Category, string? Description, [property: JsonRequired] bool IsActive, [property: JsonRequired] long ExpectedRevision);
public sealed record CatalogItem(Guid Id, string Code, string Name, string Category, string? Description, bool IsActive, long Revision);
public sealed record InventoryPage<T>(IReadOnlyList<T> Items, int? NextSkip);
public sealed record CapabilityPut([property: JsonRequired] CapabilityAvailability Availability, string? Notes, [property: JsonRequired] long ExpectedRevision);
public sealed record CapabilityItem(Guid CapabilityId, string Code, string Name, bool IsActive,
    CapabilityAvailability Availability, string? Notes, long Revision);
public sealed record AssetCreate(string Name, [property: JsonRequired] AssetKind Kind, [property: JsonRequired] AssetCriticality Criticality, string? Description);
public sealed record AssetUpdate(string Name, [property: JsonRequired] AssetKind Kind, [property: JsonRequired] AssetCriticality Criticality, string? Description,
    [property: JsonRequired] AssetStatus Status, [property: JsonRequired] long ExpectedRevision);
public sealed record AssetItem(Guid Id, Guid OrganizationId, string Name, AssetKind Kind, AssetCriticality Criticality,
    string? Description, AssetStatus Status, long Revision, DateTimeOffset CreatedAt);

public interface IInventoryService
{
    Task<InventoryPage<CatalogItem>> ListCatalogAsync(Guid actor, CatalogKind kind, bool includeInactive = false, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<CatalogItem> CreateCatalogAsync(Guid actor, CatalogKind kind, CatalogCreate request, CancellationToken ct = default);
    Task<CatalogItem> UpdateCatalogAsync(Guid actor, CatalogKind kind, Guid id, CatalogUpdate request, CancellationToken ct = default);
    Task<InventoryPage<CapabilityItem>> GetCapabilitiesAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<CapabilityItem> PutCapabilityAsync(Guid actor, Guid organization, Guid capability, CapabilityPut request, CancellationToken ct = default);
    Task<InventoryPage<AssetItem>> ListAssetsAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<AssetItem> GetAssetAsync(Guid actor, Guid organization, Guid asset, CancellationToken ct = default);
    Task<AssetItem> CreateAssetAsync(Guid actor, Guid organization, AssetCreate request, CancellationToken ct = default);
    Task<AssetItem> RetireAssetAsync(Guid actor, Guid organization, Guid asset, long expectedRevision, CancellationToken ct = default);
    Task<AssetItem> UpdateAssetAsync(Guid actor, Guid organization, Guid asset, AssetUpdate request, CancellationToken ct = default);
}
