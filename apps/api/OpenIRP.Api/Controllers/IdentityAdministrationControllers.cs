using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Identity;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    IAntiforgery antiforgery) : ControllerBase
{
    public sealed record LoginRequest([Required, EmailAddress] string Email, [Required, StringLength(128)] string Password);

    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return Unauthorized();
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        return result.Succeeded ? Ok(new { userId = user.Id }) : Unauthorized();
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return NoContent(); }
}

[Authorize, ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(IIdentityAdministration administration) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await administration.ListOrganizationsAsync(Actor, ct));
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganization request, CancellationToken ct)
    {
        var org = await administration.CreateOrganizationAsync(Actor, request, ct);
        return StatusCode(201, org);
    }
    [HttpGet("{organization:guid}/memberships")]
    public async Task<IActionResult> Members(Guid organization, CancellationToken ct) => Ok(await administration.ListMembersAsync(Actor, organization, ct));
    [HttpPost("{organization:guid}/memberships")]
    public async Task<IActionResult> Add(Guid organization, AddMember request, CancellationToken ct) =>
        StatusCode(201, await administration.AddMemberAsync(Actor, organization, request, ct));
    [HttpPatch("{organization:guid}/memberships/{membership:guid}")]
    public async Task<IActionResult> Change(Guid organization, Guid membership, ChangeMember request, CancellationToken ct) =>
        Ok(await administration.ChangeMemberAsync(Actor, organization, membership, request, ct));
}

[Authorize, ApiController]
[Route("api/users")]
public sealed class UsersController(IIdentityAdministration administration) : ControllerBase
{
    public sealed record AccountStatus(bool IsActive);
    [HttpPatch("{user:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid user, AccountStatus request, CancellationToken ct)
    {
        await administration.SetUserActiveAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), user, request.IsActive, ct);
        return NoContent();
    }
}
