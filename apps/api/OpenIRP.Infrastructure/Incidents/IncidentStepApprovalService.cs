using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task<StepApproval?> CurrentStepRequest(ExecutionTask task, CancellationToken ct)
    {
        var request = await db.StepApprovals.AsNoTracking().Where(a => a.TaskId == task.Id && a.Action == "REQUESTED")
            .OrderByDescending(a => a.ExecutionRevision).FirstOrDefaultAsync(ct);
        if (request is null || request.AssignedMembershipId != task.AssignedMembershipId ||
            await db.ExecutionTaskEvents.AnyAsync(e => e.TaskId == task.Id && e.Action == "EXECUTION_TASK_ASSIGNED" && e.Revision > request.ExecutionRevision, ct)) return null;
        return request;
    }
    private async Task<bool> HasStepApproval(ExecutionTask task, CancellationToken ct)
    {
        var request = await CurrentStepRequest(task, ct);
        return request is not null && await db.StepApprovals.AnyAsync(a => a.RequestId == request.Id && a.Action == "APPROVED", ct);
    }
    private async Task RequireApprovalTask(PlaybookExecution execution, ExecutionTask task, CancellationToken ct)
    {
        await RequireUsableExecution(execution, ct);
        if (task.Status is not ("PENDING" or "BLOCKED")) throw Error(409, "step_approval_requires_unstarted_task");
        if (!await db.PlaybookSteps.AnyAsync(s => s.Id == task.StepId && s.VersionId == execution.VersionId && s.RequiresApproval, ct))
            throw Error(409, "step_approval_not_required");
        if (task.AssignedMembershipId is not Guid assigned) throw Error(409, "execution_task_unassigned");
        await EligibleMembership(task.OrganizationId, assigned, null, ct);
        await RequireExecutionDependencies(execution, task, ct);
    }
    private void AddStepApproval(Guid actor, PlaybookExecution execution, ExecutionTask task, string action, string reason,
        Guid? request = null, bool self = false, string? justification = null)
    {
        db.StepApprovals.Add(new()
        {
            OrganizationId = execution.OrganizationId, IncidentId = execution.IncidentId, VersionId = execution.VersionId,
            ExecutionId = execution.Id, TaskId = task.Id, AssignedMembershipId = task.AssignedMembershipId!.Value,
            Action = action, ExecutionRevision = execution.Revision, ActorUserId = actor, CorrelationId = context.CorrelationId,
            Reason = reason.Trim(), RequestId = request, SelfApproval = self, SelfApprovalJustification = justification?.Trim()
        });
    }
    public Task<ExecutionItem> RequestApprovalAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        ExecutionCommand request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "STEP_APPROVAL_REQUESTED", request.ExpectedIncidentRevision, request.Reason, async _ =>
        {
            var entity = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            var item = await FindTask(entity, task, ct); await RequireApprovalTask(entity, item, ct);
            await EligibleMembership(organization, item.AssignedMembershipId!.Value, actor, ct);
            var previous = await CurrentStepRequest(item, ct);
            if (previous is not null && !await db.StepApprovals.AnyAsync(a => a.RequestId == previous.Id && a.Action == "REJECTED", ct))
                throw Error(409, "step_approval_already_requested_or_approved");
            AddStepApproval(actor, entity, item, "REQUESTED", request.Reason);
            return (entity, item);
        }, ct);

    public Task<ExecutionItem> DecideApprovalAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        Guid approvalRequest, DecideStepApproval request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, request.Approve ? "STEP_APPROVAL_APPROVED" : "STEP_APPROVAL_REJECTED",
            request.ExpectedIncidentRevision, request.Reason, async _ =>
        {
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor &&
                m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct)) throw Error(403, "organization_admin_required");
            var entity = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            var item = await FindTask(entity, task, ct); await RequireApprovalTask(entity, item, ct);
            var pending = await CurrentStepRequest(item, ct);
            if (pending is null || pending.Id != approvalRequest) throw Error(409, "step_approval_request_obsolete");
            if (await db.StepApprovals.AnyAsync(a => a.RequestId == pending.Id, ct)) throw Error(409, "step_approval_already_decided");
            var self = request.Approve && actor == pending.ActorUserId;
            if (self != request.SelfApproval || (self && (string.IsNullOrWhiteSpace(request.SelfApprovalJustification) || request.SelfApprovalJustification.Length > 2000))
                || (!self && request.SelfApprovalJustification is not null)) throw Error(400, "invalid_step_self_approval");
            AddStepApproval(actor, entity, item, request.Approve ? "APPROVED" : "REJECTED", request.Reason, pending.Id, self, request.SelfApprovalJustification);
            return (entity, item);
        }, ct);

    public Task<IReadOnlyList<StepApprovalItem>> ApprovalsAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        ExecutionRead<IReadOnlyList<StepApprovalItem>>(actor, organization, incident, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_execution_pagination");
            var entity = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution && e.OrganizationId == organization && e.IncidentId == incident, ct)
                ?? throw Error(404, "execution_not_found");
            await FindTask(entity, task, ct);
            return await db.StepApprovals.AsNoTracking().Where(a => a.TaskId == task && a.ExecutionId == execution &&
                a.OrganizationId == organization && a.IncidentId == incident && a.ExecutionRevision > afterRevision)
                .OrderBy(a => a.ExecutionRevision).Take(limit).Select(a => new StepApprovalItem(a.Id, a.RequestId, a.Action,
                    a.AssignedMembershipId, a.ExecutionRevision, a.ActorUserId, a.CorrelationId, a.Reason, a.SelfApproval,
                    a.SelfApprovalJustification, a.OccurredAt)).ToListAsync(ct);
        }, ct);
}
