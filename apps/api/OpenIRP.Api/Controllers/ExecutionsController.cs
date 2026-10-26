using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Executions;

namespace OpenIRP.Api.Controllers;

[Authorize, ApiController, Route("api/organizations/{organization:guid}/incidents/{incident:guid}/executions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExecutionsController(IExecutionService service) : ControllerBase
{
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
}
