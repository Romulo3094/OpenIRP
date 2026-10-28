using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task RequireSkipTask(PlaybookExecution execution, ExecutionTask task, CancellationToken ct)
    {
        await RequireUsableExecution(execution, ct);
        if (task.Status is not ("PENDING" or "BLOCKED")) throw Error(409, "skip_requires_unstarted_or_blocked_task");
        if (task.AssignedMembershipId is not Guid assigned) throw Error(409, "execution_task_unassigned");
        await EligibleMembership(task.OrganizationId, assigned, null, ct);
    }

    private Task<string> SkipDependencyContext(Guid execution, Guid task, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"""SELECT app.skip_dependency_context({execution}, {task})::text AS "Value" """).SingleAsync(ct);

    private static bool SkipRequestCurrent(SkipAuthorization request, PlaybookExecution execution, Incident incident) =>
        request.ExecutionRevision == execution.Revision - 1 && request.IncidentRevision == incident.Revision - 1;

    private void AddSkipAuthorization(Guid actor, Incident incident, PlaybookExecution execution, ExecutionTask task,
        string action, string reason, string impact, string risk, string dependencies,
        Guid? request = null, bool self = false, string? justification = null) =>
        db.SkipAuthorizations.Add(new()
        {
            OrganizationId = execution.OrganizationId, IncidentId = execution.IncidentId, VersionId = execution.VersionId,
            ExecutionId = execution.Id, TaskId = task.Id, AssignedMembershipId = task.AssignedMembershipId!.Value,
            ExecutionRevision = execution.Revision, IncidentRevision = incident.Revision, ActorUserId = actor,
            CorrelationId = context.CorrelationId, Action = action, Reason = reason.Trim(), Impact = impact.Trim(),
            ResidualRisk = risk.Trim(), DependencyContextJson = dependencies, RequestId = request,
            SelfApproval = self, SelfApprovalJustification = justification?.Trim()
        });

    public Task<ExecutionItem> RequestSkipAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        RequestSkipAuthorization request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "SKIP_AUTHORIZATION_REQUESTED", request.ExpectedIncidentRevision, request.Reason, async entity =>
        {
            Reason(request.Impact); Reason(request.ResidualRisk);
            var run = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            var item = await FindTask(run, task, ct); await RequireSkipTask(run, item, ct);
            await EligibleMembership(organization, item.AssignedMembershipId!.Value, actor, ct);
            var previous = await db.SkipAuthorizations.AsNoTracking().Where(a => a.TaskId == task && a.Action == "REQUESTED")
                .OrderByDescending(a => a.ExecutionRevision).FirstOrDefaultAsync(ct);
            if (previous is not null && SkipRequestCurrent(previous, run, entity)
                && !await db.SkipAuthorizations.AnyAsync(a => a.RequestId == previous.Id && a.Action == "REJECTED", ct))
                throw Error(409, "skip_authorization_already_pending");
            AddSkipAuthorization(actor, entity, run, item, "REQUESTED", request.Reason, request.Impact,
                request.ResidualRisk, await SkipDependencyContext(execution, task, ct));
            return (run, item);
        }, ct);

    public Task<ExecutionItem> DecideSkipAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        Guid authorizationRequest, DecideSkipAuthorization request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, request.Approve ? "SKIP_AUTHORIZATION_APPROVED" : "SKIP_AUTHORIZATION_REJECTED",
            request.ExpectedIncidentRevision, request.Reason, async entity =>
        {
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor &&
                m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct))
                throw Error(403, "organization_admin_required");
            var run = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            var item = await FindTask(run, task, ct); await RequireSkipTask(run, item, ct);
            var pending = await db.SkipAuthorizations.AsNoTracking().Where(a => a.TaskId == task && a.Action == "REQUESTED")
                .OrderByDescending(a => a.ExecutionRevision).FirstOrDefaultAsync(ct);
            if (pending is null || pending.Id != authorizationRequest) throw Error(409, "skip_authorization_request_obsolete");
            if (await db.SkipAuthorizations.AnyAsync(a => a.RequestId == pending.Id, ct)) throw Error(409, "skip_authorization_already_decided");
            if (!SkipRequestCurrent(pending, run, entity) || pending.AssignedMembershipId != item.AssignedMembershipId)
                throw Error(409, "skip_authorization_context_changed");
            var self = request.Approve && actor == pending.ActorUserId;
            if (self != request.SelfApproval || (self && (string.IsNullOrWhiteSpace(request.SelfApprovalJustification) || request.SelfApprovalJustification.Length > 2000))
                || (!self && request.SelfApprovalJustification is not null)) throw Error(400, "invalid_skip_self_approval");
            AddSkipAuthorization(actor, entity, run, item, request.Approve ? "APPROVED" : "REJECTED", request.Reason,
                pending.Impact, pending.ResidualRisk, pending.DependencyContextJson, pending.Id, self, request.SelfApprovalJustification);
            if (request.Approve) { item.Status = "SKIPPED"; item.ChangedAtRevision = run.Revision; }
            return (run, item);
        }, ct);

    public Task<IReadOnlyList<SkipAuthorizationItem>> SkipAuthorizationsAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        ExecutionRead<IReadOnlyList<SkipAuthorizationItem>>(actor, organization, incident, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_execution_pagination");
            var run = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution && e.OrganizationId == organization && e.IncidentId == incident, ct)
                ?? throw Error(404, "execution_not_found");
            await FindTask(run, task, ct);
            var rows = await db.SkipAuthorizations.AsNoTracking().Where(a => a.TaskId == task && a.ExecutionId == execution &&
                a.OrganizationId == organization && a.IncidentId == incident && a.ExecutionRevision > afterRevision)
                .OrderBy(a => a.ExecutionRevision).Take(limit).ToListAsync(ct);
            return rows.Select(a => new SkipAuthorizationItem(a.Id, a.RequestId, a.Action, a.AssignedMembershipId,
                a.ExecutionRevision, a.IncidentRevision, a.ActorUserId, a.CorrelationId, a.Reason, a.Impact, a.ResidualRisk,
                JsonSerializer.Deserialize<JsonElement>(a.DependencyContextJson), a.SelfApproval, a.SelfApprovalJustification, a.OccurredAt)).ToList();
        }, ct);
}
