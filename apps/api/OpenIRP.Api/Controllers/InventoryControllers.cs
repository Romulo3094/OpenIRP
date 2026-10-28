using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Inventory;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/catalogs/{catalog}")]
public sealed class CatalogsController(IInventoryService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static CatalogKind Kind(string catalog) => catalog switch
    {
        "capabilities" => CatalogKind.Capabilities,
        "incident-categories" => CatalogKind.IncidentCategories,
        _ => throw new IdentityOperationException(404, "catalog_not_found")
    };
    [HttpGet]
    public async Task<IActionResult> List(string catalog, [FromQuery] bool includeInactive = false, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListCatalogAsync(Actor, Kind(catalog), includeInactive, skip, limit, ct));
    [HttpPost]
    public async Task<IActionResult> Create(string catalog, CatalogCreate request, CancellationToken ct) =>
        StatusCode(201, await service.CreateCatalogAsync(Actor, Kind(catalog), request, ct));
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(string catalog, Guid id, CatalogUpdate request, CancellationToken ct) =>
        Ok(await service.UpdateCatalogAsync(Actor, Kind(catalog), id, request, ct));
}

[Authorize, ApiController, Route("api/organizations/{organization:guid}")]
public sealed class InventoryController(IInventoryService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(Guid organization, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.GetCapabilitiesAsync(Actor, organization, skip, limit, ct));
    [HttpPut("capabilities/{capability:guid}")]
    public async Task<IActionResult> SetCapability(Guid organization, Guid capability, CapabilityPut request, CancellationToken ct) =>
        Ok(await service.PutCapabilityAsync(Actor, organization, capability, request, ct));
    [HttpGet("assets")]
    public async Task<IActionResult> Assets(Guid organization, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListAssetsAsync(Actor, organization, skip, limit, ct));
    [HttpGet("assets/{asset:guid}")]
    public async Task<IActionResult> Asset(Guid organization, Guid asset, CancellationToken ct) =>
        Ok(await service.GetAssetAsync(Actor, organization, asset, ct));
    [HttpPost("assets")]
    public async Task<IActionResult> CreateAsset(Guid organization, AssetCreate request, CancellationToken ct) =>
        StatusCode(201, await service.CreateAssetAsync(Actor, organization, request, ct));
    [HttpPut("assets/{asset:guid}")]
    public async Task<IActionResult> UpdateAsset(Guid organization, Guid asset, AssetUpdate request, CancellationToken ct) =>
        Ok(await service.UpdateAssetAsync(Actor, organization, asset, request, ct));
    [HttpDelete("assets/{asset:guid}")]
    public async Task<IActionResult> RetireAsset(Guid organization, Guid asset, [FromQuery] long expectedRevision, CancellationToken ct) =>
        Ok(await service.RetireAssetAsync(Actor, organization, asset, expectedRevision, ct));
}
