using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentWorkflowController(IIncidentService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPut("state")]
    public async Task<IActionResult> Transition(Guid organization, Guid incident, TransitionIncident request, CancellationToken ct) =>
        Ok(await service.TransitionAsync(Actor, organization, incident, request, ct));
    [HttpPost("closure-requests")]
    public async Task<IActionResult> RequestClosure(Guid organization, Guid incident, RequestIncidentClosure request, CancellationToken ct) =>
        Created($"/api/organizations/{organization}/incidents/{incident}", await service.RequestClosureAsync(Actor, organization, incident, request, ct));
    [HttpPut("closure-requests/{closureRequest:guid}")]
    public async Task<IActionResult> Decide(Guid organization, Guid incident, Guid closureRequest, DecideIncidentClosure request, CancellationToken ct) =>
        Ok(await service.DecideClosureAsync(Actor, organization, incident, closureRequest, request, ct));
}
