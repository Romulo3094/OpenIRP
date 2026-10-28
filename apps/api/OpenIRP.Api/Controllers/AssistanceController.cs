using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Assistance;
namespace OpenIRP.Api.Controllers;

[Authorize,ApiController,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/organizations/{organization:guid}/incidents/{incident:guid}/executions/{execution:guid}/tasks/{task:guid}")]
public sealed class AssistanceController(IAssistanceService service):ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("assistance-context")]
    public async Task<ActionResult<AssistanceContext>> Context(Guid organization,Guid incident,Guid execution,Guid task,CancellationToken ct)=>
        Ok(await service.AssistanceContextAsync(Actor,organization,incident,execution,task,ct));
    [HttpPost("assistance")]
    public async Task<ActionResult<AssistanceAssessmentItem>> Explain(Guid organization,Guid incident,Guid execution,Guid task,ExplainStep request,CancellationToken ct)=>
        Ok(await service.ExplainStepAsync(Actor,organization,incident,execution,task,request,ct));
    [HttpGet("assistance-assessments")]
    public async Task<ActionResult<AssistanceAssessmentPage>> History(Guid organization,Guid incident,Guid execution,Guid task,int skip=0,int limit=50,CancellationToken ct=default)=>
        Ok(await service.AssistanceHistoryAsync(Actor,organization,incident,execution,task,skip,limit,ct));
}
