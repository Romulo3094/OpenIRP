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
    [HttpGet("closure-readiness")]
    public Task<IncidentClosureReadiness> Readiness(Guid organization, Guid incident, CancellationToken ct) =>
        service.ClosureReadinessAsync(Actor, organization, incident, ct);

    [HttpPost("closure-risk-reviews")]
    public Task<ClosureRiskReviewItem> ReviewRisk(Guid organization, Guid incident, ReviewClosureRisk request, CancellationToken ct) =>
        service.ReviewClosureRiskAsync(Actor, organization, incident, request, ct);
    [HttpGet("closure-risk-reviews")]
    public Task<IReadOnlyList<ClosureRiskReviewItem>> RiskHistory(Guid organization, Guid incident, [FromQuery] long afterRevision = 0, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        service.ClosureRiskHistoryAsync(Actor, organization, incident, afterRevision, limit, ct);

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
