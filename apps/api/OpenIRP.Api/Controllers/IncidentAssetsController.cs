using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}/assets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentAssetsController(IIncidentService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, Guid incident, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.AssetsAsync(Actor, organization, incident, skip, limit, ct));
    [HttpPost("reported")]
    public async Task<IActionResult> Report(Guid organization, Guid incident, ReportIncidentAsset request, CancellationToken ct) =>
        StatusCode(201, await service.ReportAssetAsync(Actor, organization, incident, request, ct));
    [HttpPost("from-inventory")]
    public async Task<IActionResult> Attach(Guid organization, Guid incident, AttachIncidentAsset request, CancellationToken ct) =>
        StatusCode(201, await service.AttachAssetAsync(Actor, organization, incident, request, ct));
    [HttpPut("{association:guid}/link")]
    public async Task<IActionResult> Link(Guid organization, Guid incident, Guid association, LinkIncidentAsset request, CancellationToken ct) =>
        Ok(await service.LinkAssetAsync(Actor, organization, incident, association, request, ct));
}
