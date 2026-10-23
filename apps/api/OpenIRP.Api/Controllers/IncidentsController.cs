using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentsController(IIncidentService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, [FromQuery] int skip = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListAsync(Actor, organization, skip, limit, ct));
    [HttpGet("{incident:guid}")]
    public async Task<IActionResult> Get(Guid organization, Guid incident, CancellationToken ct) =>
        Ok(await service.GetAsync(Actor, organization, incident, ct));
    [HttpPost]
    public async Task<IActionResult> Create(Guid organization, IncidentCreate request, CancellationToken ct)
    {
        var item = await service.CreateAsync(Actor, organization, request, ct);
        return CreatedAtAction(nameof(Get), new { organization, incident = item.Id }, item);
    }
    [HttpPut("{incident:guid}")]
    public async Task<IActionResult> Update(Guid organization, Guid incident, IncidentUpdate request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(Actor, organization, incident, request, ct));
    [HttpPut("{incident:guid}/assignment")]
    public async Task<IActionResult> Assign(Guid organization, Guid incident, IncidentAssignment request, CancellationToken ct) =>
        Ok(await service.AssignAsync(Actor, organization, incident, request, ct));
    [HttpGet("{incident:guid}/events")]
    public async Task<IActionResult> Events(Guid organization, Guid incident, [FromQuery] long afterRevision = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await service.EventsAsync(Actor, organization, incident, afterRevision, limit, ct));
}
