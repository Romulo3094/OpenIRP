using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}/capabilities")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentCapabilitiesController(IIncidentService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, Guid incident, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.CapabilitiesAsync(Actor, organization, incident, skip, limit, ct));
    [HttpPut("{capability:guid}")]
    public async Task<IActionResult> Assess(Guid organization, Guid incident, Guid capability, AssessIncidentCapability request, CancellationToken ct) =>
        Ok(await service.AssessCapabilityAsync(Actor, organization, incident, capability, request, ct));
}
