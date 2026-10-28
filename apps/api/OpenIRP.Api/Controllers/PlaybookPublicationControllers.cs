using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Playbooks;

namespace OpenIRP.Api.Controllers;

public sealed partial class PublicPlaybookDraftController
{
    [HttpGet("{playbook:guid}/versions")]
    public async Task<IActionResult> Versions(Guid playbook, int afterNumber = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.VersionsAsync(Actor, null, playbook, afterNumber, limit, ct));
    [HttpGet("{playbook:guid}/versions/{version:guid}")]
    public async Task<IActionResult> Version(Guid playbook, Guid version, CancellationToken ct) =>
        Ok(await service.VersionAsync(Actor, null, playbook, version, ct));
    [HttpGet("{playbook:guid}/reviews")]
    public async Task<IActionResult> Reviews(Guid playbook, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ReviewsAsync(Actor, null, playbook, afterRevision, limit, ct));
    [HttpPost("{playbook:guid}/versions")]
    public async Task<IActionResult> NewVersion(Guid playbook, NewPlaybookVersion request, CancellationToken ct) =>
        StatusCode(201, await service.NewVersionAsync(Actor, null, playbook, request, ct));
    [HttpPost("{playbook:guid}/versions/{version:guid}/review-requests")]
    public async Task<IActionResult> Submit(Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct) =>
        StatusCode(201, await service.SubmitAsync(Actor, null, playbook, version, request, ct));
    [HttpPut("{playbook:guid}/versions/{version:guid}/review-requests/{reviewRequest:guid}")]
    public async Task<IActionResult> Decide(Guid playbook, Guid version, Guid reviewRequest, DecidePlaybookPublication request, CancellationToken ct) =>
        Ok(await service.DecideAsync(Actor, null, playbook, version, reviewRequest, request, ct));
    [HttpPost("{playbook:guid}/versions/{version:guid}/withdrawals")]
    public async Task<IActionResult> Withdraw(Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct) =>
        Ok(await service.WithdrawAsync(Actor, null, playbook, version, request, ct));

}

public sealed partial class PrivatePlaybookDraftController
{
    [HttpGet("{playbook:guid}/versions")]
    public async Task<IActionResult> Versions(Guid organization, Guid playbook, int afterNumber = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.VersionsAsync(Actor, organization, playbook, afterNumber, limit, ct));
    [HttpGet("{playbook:guid}/versions/{version:guid}")]
    public async Task<IActionResult> Version(Guid organization, Guid playbook, Guid version, CancellationToken ct) =>
        Ok(await service.VersionAsync(Actor, organization, playbook, version, ct));
    [HttpGet("{playbook:guid}/reviews")]
    public async Task<IActionResult> Reviews(Guid organization, Guid playbook, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ReviewsAsync(Actor, organization, playbook, afterRevision, limit, ct));
    [HttpPost("{playbook:guid}/versions")]
    public async Task<IActionResult> NewVersion(Guid organization, Guid playbook, NewPlaybookVersion request, CancellationToken ct) =>
        StatusCode(201, await service.NewVersionAsync(Actor, organization, playbook, request, ct));
    [HttpPost("{playbook:guid}/versions/{version:guid}/review-requests")]
    public async Task<IActionResult> Submit(Guid organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct) =>
        StatusCode(201, await service.SubmitAsync(Actor, organization, playbook, version, request, ct));
    [HttpPut("{playbook:guid}/versions/{version:guid}/review-requests/{reviewRequest:guid}")]
    public async Task<IActionResult> Decide(Guid organization, Guid playbook, Guid version, Guid reviewRequest, DecidePlaybookPublication request, CancellationToken ct) =>
        Ok(await service.DecideAsync(Actor, organization, playbook, version, reviewRequest, request, ct));
    [HttpPost("{playbook:guid}/versions/{version:guid}/withdrawals")]
    public async Task<IActionResult> Withdraw(Guid organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct) =>
        Ok(await service.WithdrawAsync(Actor, organization, playbook, version, request, ct));
    [HttpGet("published")]
    public async Task<IActionResult> Published(Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListCurrentAsync(Actor, organization, skip, limit, ct));
    [HttpGet("{playbook:guid}/current")]
    public async Task<IActionResult> Current(Guid organization, Guid playbook, CancellationToken ct) =>
        Ok(await service.CurrentAsync(Actor, organization, playbook, ct));
}

[Authorize, ApiController, Route("api/playbooks/public")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublishedPlaybooksController(IPlaybookDraftService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListCurrentAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), null, skip, limit, ct));
    [HttpGet("{playbook:guid}/current")]
    public async Task<IActionResult> Current(Guid playbook, CancellationToken ct) =>
        Ok(await service.CurrentAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), null, playbook, ct));
}
