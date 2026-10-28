using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task<DependencyException> FindDependencyApproval(Guid organization, Guid incident, Guid execution, Guid task,
        Guid predecessor, Guid approval, CancellationToken ct) => await db.DependencyExceptions.AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == approval && a.OrganizationId == organization && a.IncidentId == incident && a.ExecutionId == execution &&
            a.TaskId == task && a.PredecessorTaskId == predecessor && a.Action == "APPROVED", ct)
            ?? throw Error(404, "dependency_exception_approval_not_found");

    public Task<ExecutionItem> RevokeDependencyExceptionAsync(Guid actor, Guid organization, Guid incident, Guid execution,
        Guid task, Guid predecessor, Guid exceptionApproval, ExecutionCommand request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "DEPENDENCY_EXCEPTION_REVOKED", request.ExpectedIncidentRevision, request.Reason, async entity =>
        {
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor &&
                m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct))
                throw Error(403, "organization_admin_required");
            var run = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            await RequireUsableExecution(run, ct);
            var item = await FindTask(run, task, ct);
            if (item.Status is not ("PENDING" or "BLOCKED" or "IN_PROGRESS")) throw Error(409, "dependency_revocation_requires_unfinished_task");
            var approval = await FindDependencyApproval(organization, incident, execution, task, predecessor, exceptionApproval, ct);
            if (await db.DependencyExceptionRevocations.AnyAsync(r => r.ApprovalId == exceptionApproval, ct))
                throw Error(409, "dependency_exception_already_revoked");
            var blocked = item.Status == "IN_PROGRESS";
            if (blocked) { item.Status = "BLOCKED"; item.ChangedAtRevision = run.Revision; }
            db.DependencyExceptionRevocations.Add(new()
            {
                OrganizationId = organization, IncidentId = incident, VersionId = run.VersionId, ExecutionId = execution,
                TaskId = task, DependencyId = approval.DependencyId, PredecessorTaskId = predecessor,
                SkipAuthorizationId = approval.SkipAuthorizationId, AssignedMembershipId = approval.AssignedMembershipId,
                ApprovalId = approval.Id, ExecutionRevision = run.Revision, IncidentRevision = entity.Revision,
                ActorUserId = actor, CorrelationId = context.CorrelationId, Reason = request.Reason.Trim(), BlockedTask = blocked
            });
            return (run, item);
        }, ct);

    public Task<DependencyRevocationItem> DependencyRevocationAsync(Guid actor, Guid organization, Guid incident, Guid execution,
        Guid task, Guid predecessor, Guid exceptionApproval, CancellationToken ct = default) =>
        ExecutionRead(actor, organization, incident, async () =>
        {
            var run = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution && e.OrganizationId == organization && e.IncidentId == incident, ct)
                ?? throw Error(404, "execution_not_found");
            await FindTask(run, task, ct);
            await FindDependencyApproval(organization, incident, execution, task, predecessor, exceptionApproval, ct);
            var revocation = await db.DependencyExceptionRevocations.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organization &&
                r.IncidentId == incident && r.ExecutionId == execution && r.TaskId == task && r.PredecessorTaskId == predecessor && r.ApprovalId == exceptionApproval, ct)
                ?? throw Error(404, "dependency_revocation_not_found");
            return new DependencyRevocationItem(revocation.Id, revocation.ApprovalId, revocation.TaskId, revocation.PredecessorTaskId,
                revocation.ExecutionRevision, revocation.IncidentRevision, revocation.ActorUserId, revocation.CorrelationId,
                revocation.Reason, revocation.BlockedTask, revocation.OccurredAt);
        }, ct);
}
