using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}/context")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentContextController(IIncidentService service) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Assess(Guid organization, Guid incident, AssessIncidentContext request, CancellationToken ct) =>
        Ok(await service.AssessContextAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), organization, incident, request, ct));
}
