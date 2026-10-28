using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Playbooks;

namespace OpenIRP.Api.Controllers;

public sealed partial class PublicPlaybookDraftController
{
    [HttpPost("clones")]
    public async Task<IActionResult> Clone(ClonePlaybook request, CancellationToken ct)
    {
        var item = await service.CloneAsync(Actor, null, request, ct);
        return Created($"/api/platform/playbooks/{item.Playbook.Id}", item);
    }
    [HttpGet("{playbook:guid}/origin")]
    public async Task<IActionResult> Origin(Guid playbook, CancellationToken ct) =>
        Ok(await service.OriginAsync(Actor, null, playbook, ct));
}

public sealed partial class PrivatePlaybookDraftController
{
    [HttpPost("clones")]
    public async Task<IActionResult> Clone(Guid organization, ClonePlaybook request, CancellationToken ct)
    {
        var item = await service.CloneAsync(Actor, organization, request, ct);
        return Created($"/api/organizations/{organization}/playbooks/{item.Playbook.Id}", item);
    }
    [HttpGet("{playbook:guid}/origin")]
    public async Task<IActionResult> Origin(Guid organization, Guid playbook, CancellationToken ct) =>
        Ok(await service.OriginAsync(Actor, organization, playbook, ct));
}
