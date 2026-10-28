using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize,ApiController,Route("api/organizations/{organization:guid}/incidents/{incident:guid}/evidence")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class EvidenceController(IEvidenceService service):ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization,Guid incident,int skip=0,int limit=50,CancellationToken ct=default) =>
        Ok(await service.ListEvidenceAsync(Actor,organization,incident,skip,limit,ct));
    [HttpPost]
    public async Task<IActionResult> Register(Guid organization,Guid incident,RegisterEvidence request,CancellationToken ct)
    {
        var result=await service.RegisterAsync(Actor,organization,incident,request,ct);
        return Created($"/api/organizations/{organization}/incidents/{incident}/evidence/{result.Evidence.Id}",result);
    }
    [HttpGet("{evidence:guid}")]
    public async Task<IActionResult> Get(Guid organization,Guid incident,Guid evidence,CancellationToken ct)=>
        Ok(await service.GetEvidenceAsync(Actor,organization,incident,evidence,ct));
    [HttpGet("{evidence:guid}/events")]
    public async Task<IActionResult> Events(Guid organization,Guid incident,Guid evidence,long afterRevision=0,int limit=50,CancellationToken ct=default)=>
        Ok(await service.EvidenceEventsAsync(Actor,organization,incident,evidence,afterRevision,limit,ct));
    [HttpPost("{evidence:guid}/withdrawal")]
    public async Task<IActionResult> Withdraw(Guid organization,Guid incident,Guid evidence,WithdrawEvidence request,CancellationToken ct)=>
        Ok(await service.WithdrawAsync(Actor,organization,incident,evidence,request,ct));
}
