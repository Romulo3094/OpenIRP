using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Executions;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}/executions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExecutionsController(IExecutionService service) : ControllerBase
{
    [HttpGet("{execution:guid}/operational-reviews")]
    public async Task<ActionResult<OperationalReviewPage>> OperationalReviews(Guid organization,Guid incident,Guid execution,long afterRevision=0,int limit=50,CancellationToken ct=default) =>
        Ok(await service.OperationalReviewsAsync(Actor,organization,incident,execution,afterRevision,limit,ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/operational-reviews")]
    public async Task<ActionResult<OperationalReviewItem>> ReviewOperation(Guid organization,Guid incident,Guid execution,Guid task,RegisterOperationalReview request,CancellationToken ct) =>
        Ok(await service.ReviewOperationAsync(Actor,organization,incident,execution,task,request,ct));
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List(Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ListAsync(Actor, organization, incident, skip, limit, ct));
    [HttpPost]
    public async Task<IActionResult> Start(Guid organization, Guid incident, StartPlaybookExecution request, CancellationToken ct)
    {
        var item = await service.StartAsync(Actor, organization, incident, request, ct);
        return Created($"/api/organizations/{organization}/incidents/{incident}/executions/{item.Id}", item);
    }
    [HttpGet("{execution:guid}")]
    public async Task<IActionResult> Get(Guid organization, Guid incident, Guid execution, CancellationToken ct) =>
        Ok(await service.GetAsync(Actor, organization, incident, execution, ct));
    [HttpGet("{execution:guid}/events")]
    public async Task<IActionResult> Events(Guid organization, Guid incident, Guid execution, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.EventsAsync(Actor, organization, incident, execution, afterRevision, limit, ct));
    [HttpPut("{execution:guid}/tasks/{task:guid}/assignment")]
    public async Task<IActionResult> Assign(Guid organization, Guid incident, Guid execution, Guid task, AssignExecutionTask request, CancellationToken ct) =>
        Ok(await service.AssignAsync(Actor, organization, incident, execution, task, request, ct));
    [HttpPut("{execution:guid}/tasks/{task:guid}/state")]
    public async Task<IActionResult> Change(Guid organization, Guid incident, Guid execution, Guid task, ChangeExecutionTask request, CancellationToken ct) =>
        Ok(await service.ChangeAsync(Actor, organization, incident, execution, task, request, ct));
    [HttpPost("{execution:guid}/completion")]
    public async Task<IActionResult> Complete(Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct) =>
        Ok(await service.CompleteAsync(Actor, organization, incident, execution, request, ct));
    [HttpPost("{execution:guid}/cancellation")]
    public async Task<IActionResult> Cancel(Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct) =>
        Ok(await service.CancelAsync(Actor, organization, incident, execution, request, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/approvals")]
    public async Task<IActionResult> Approvals(Guid organization, Guid incident, Guid execution, Guid task, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.ApprovalsAsync(Actor, organization, incident, execution, task, afterRevision, limit, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/approvals")]
    public async Task<IActionResult> RequestApproval(Guid organization, Guid incident, Guid execution, Guid task, ExecutionCommand request, CancellationToken ct) =>
        Ok(await service.RequestApprovalAsync(Actor, organization, incident, execution, task, request, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/approvals/{approvalRequest:guid}/decision")]
    public async Task<IActionResult> DecideApproval(Guid organization, Guid incident, Guid execution, Guid task, Guid approvalRequest, DecideStepApproval request, CancellationToken ct) =>
        Ok(await service.DecideApprovalAsync(Actor, organization, incident, execution, task, approvalRequest, request, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/skip-authorizations")]
    public async Task<IActionResult> SkipAuthorizations(Guid organization, Guid incident, Guid execution, Guid task, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.SkipAuthorizationsAsync(Actor, organization, incident, execution, task, afterRevision, limit, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/skip-authorizations")]
    public async Task<IActionResult> RequestSkip(Guid organization, Guid incident, Guid execution, Guid task, RequestSkipAuthorization request, CancellationToken ct) =>
        Ok(await service.RequestSkipAsync(Actor, organization, incident, execution, task, request, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/skip-authorizations/{authorizationRequest:guid}/decision")]
    public async Task<IActionResult> DecideSkip(Guid organization, Guid incident, Guid execution, Guid task, Guid authorizationRequest, DecideSkipAuthorization request, CancellationToken ct) =>
        Ok(await service.DecideSkipAsync(Actor, organization, incident, execution, task, authorizationRequest, request, ct));
    [HttpGet("{execution:guid}/evidence-readiness")]
    public async Task<ActionResult<ExecutionEvidenceReadiness>> EvidenceReadiness(Guid organization, Guid incident, Guid execution, CancellationToken ct) =>
        Ok(await service.EvidenceReadinessAsync(Actor, organization, incident, execution, ct));

    [HttpGet("{execution:guid}/capability-readiness")]
    public async Task<ActionResult<ExecutionCapabilityReadiness>> CapabilityReadiness(Guid organization, Guid incident, Guid execution, CancellationToken ct) =>
        Ok(await service.CapabilityReadinessAsync(Actor, organization, incident, execution, null, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/capability-readiness")]
    public async Task<ActionResult<ExecutionCapabilityReadiness>> TaskCapabilityReadiness(Guid organization, Guid incident, Guid execution, Guid task, CancellationToken ct) =>
        Ok(await service.CapabilityReadinessAsync(Actor, organization, incident, execution, task, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/dependencies/{predecessor:guid}/exceptions")]
    public async Task<ActionResult<IReadOnlyList<DependencyExceptionItem>>> DependencyExceptions(Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Ok(await service.DependencyExceptionsAsync(Actor, organization, incident, execution, task, predecessor, afterRevision, limit, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/dependencies/{predecessor:guid}/exceptions")]
    public async Task<ActionResult<ExecutionItem>> RequestDependencyException(Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor, RequestDependencyException request, CancellationToken ct) =>
        Ok(await service.RequestDependencyExceptionAsync(Actor, organization, incident, execution, task, predecessor, request, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/dependencies/{predecessor:guid}/exceptions/{exceptionRequest:guid}/decision")]
    public async Task<ActionResult<ExecutionItem>> DecideDependencyException(Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor, Guid exceptionRequest, DecideDependencyException request, CancellationToken ct) =>
        Ok(await service.DecideDependencyExceptionAsync(Actor, organization, incident, execution, task, predecessor, exceptionRequest, request, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/dependency-readiness")]
    public async Task<ActionResult<TaskDependencyReadiness>> DependencyReadiness(Guid organization, Guid incident, Guid execution, Guid task, CancellationToken ct) =>
        Ok(await service.DependencyReadinessAsync(Actor, organization, incident, execution, task, ct));
    [HttpPost("{execution:guid}/tasks/{task:guid}/dependencies/{predecessor:guid}/exceptions/{exceptionApproval:guid}/revocation")]
    public async Task<ActionResult<ExecutionItem>> RevokeDependencyException(Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor, Guid exceptionApproval, ExecutionCommand request, CancellationToken ct) =>
        Ok(await service.RevokeDependencyExceptionAsync(Actor, organization, incident, execution, task, predecessor, exceptionApproval, request, ct));
    [HttpGet("{execution:guid}/tasks/{task:guid}/dependencies/{predecessor:guid}/exceptions/{exceptionApproval:guid}/revocation")]
    public async Task<ActionResult<DependencyRevocationItem>> DependencyRevocation(Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor, Guid exceptionApproval, CancellationToken ct) =>
        Ok(await service.DependencyRevocationAsync(Actor, organization, incident, execution, task, predecessor, exceptionApproval, ct));
    [HttpGet("{execution:guid}/security-hold")]
    public async Task<ActionResult<ExecutionSecurityState>> SecurityHold(Guid organization,Guid incident,Guid execution,CancellationToken ct)=>
        Ok(await service.SecurityStateAsync(Actor,organization,incident,execution,ct));
    [HttpPost("{execution:guid}/security-holds/{securityHold:guid}/decision")]
    public async Task<ActionResult<ExecutionItem>> DecideSecurityHold(Guid organization,Guid incident,Guid execution,Guid securityHold,DecideSecurityHold request,CancellationToken ct)=>
        Ok(await service.DecideSecurityHoldAsync(Actor,organization,incident,execution,securityHold,request,ct));
    [HttpGet("{execution:guid}/security-decisions")]
    public async Task<ActionResult<IReadOnlyList<SecurityDecisionItem>>> SecurityDecisions(Guid organization,Guid incident,Guid execution,long afterRevision=0,int limit=50,CancellationToken ct=default)=>
        Ok(await service.SecurityDecisionsAsync(Actor,organization,incident,execution,afterRevision,limit,ct));
}
