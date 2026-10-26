using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Playbooks;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/platform/playbooks")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1048576)]
public sealed partial class PublicPlaybookDraftController(IPlaybookDraftService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListAsync(Actor, null, skip, limit, ct));
    [HttpGet("{playbook:guid}")]
    public async Task<IActionResult> Get(Guid playbook, CancellationToken ct) =>
        Ok(await service.GetAsync(Actor, null, playbook, ct));
    [HttpPost]
    public async Task<IActionResult> Create(CreatePlaybookDraft request, CancellationToken ct)
    {
        var item = await service.CreateAsync(Actor, null, request, ct);
        return Created($"/api/platform/playbooks/{item.Playbook.Id}", item);
    }
    [HttpPut("{playbook:guid}/draft")]
    public async Task<IActionResult> Update(Guid playbook, UpdatePlaybookDraft request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(Actor, null, playbook, request, ct));
}

[Authorize, ApiController, Route("api/organizations/{organization:guid}/playbooks")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1048576)]
public sealed partial class PrivatePlaybookDraftController(IPlaybookDraftService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListAsync(Actor, organization, skip, limit, ct));
    [HttpGet("{playbook:guid}")]
    public async Task<IActionResult> Get(Guid organization, Guid playbook, CancellationToken ct) =>
        Ok(await service.GetAsync(Actor, organization, playbook, ct));
    [HttpPost]
    public async Task<IActionResult> Create(Guid organization, CreatePlaybookDraft request, CancellationToken ct)
    {
        var item = await service.CreateAsync(Actor, organization, request, ct);
        return Created($"/api/organizations/{organization}/playbooks/{item.Playbook.Id}", item);
    }
    [HttpPut("{playbook:guid}/draft")]
    public async Task<IActionResult> Update(Guid organization, Guid playbook, UpdatePlaybookDraft request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(Actor, organization, playbook, request, ct));
}
