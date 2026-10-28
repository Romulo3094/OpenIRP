using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Audit;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController]
public sealed class AuditController(IAuditReader reader) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("api/organizations/{organization:guid}/audit")]
    public async Task<IActionResult> Organization(Guid organization, [FromQuery] long? before, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await reader.ReadOrganizationAsync(Actor, organization, before, limit, ct));
    }

    [HttpGet("api/audit/global")]
    public async Task<IActionResult> Global([FromQuery] long? before, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await reader.ReadGlobalAsync(Actor, before, limit, ct));
    }
}
