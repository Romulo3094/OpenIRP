using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task<ExecutionItem> ExecutionWrite(Guid actor, Guid organization, Guid incident, string action,
        long expectedIncident, string reason, Func<Incident, Task<(PlaybookExecution Execution, ExecutionTask? Task)>> operation, CancellationToken ct)
    {
        ExecutionItem? receipt = null;
        object? payload = null;
        await WriteAsync(actor, organization, incident, action, async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(reason); Next(entity, expectedIncident);
            if (entity.Status != "IN_RESPONSE") throw Error(409, "incident_not_in_response");
            var (execution, task) = await operation(entity);
            db.ExecutionTaskEvents.Add(new()
            {
                OrganizationId = organization, IncidentId = incident, ExecutionId = execution.Id, VersionId = execution.VersionId,
                TaskId = task?.Id, Revision = execution.Revision, IncidentRevision = entity.Revision,
                ExecutionStatus = execution.Status, TaskStatus = task?.Status, AssignedMembershipId = task?.AssignedMembershipId,
                ActorUserId = actor, CorrelationId = context.CorrelationId, Action = action, Reason = reason.Trim()
            });
            payload = new { execution = new { executionId = execution.Id, execution.VersionId, executionRevision = execution.Revision,
                taskId = task?.Id, taskStatus = task?.Status, completionEvidenceId = task?.CompletionEvidenceId } };
            receipt = await ExecutionView(execution, entity.Revision, ct);
            return (entity, reason.Trim());
        }, ct, () => JsonSerializer.Serialize(payload));
        return receipt!;
    }

    public Task<ExecutionItem> StartAsync(Guid actor, Guid organization, Guid incident, StartPlaybookExecution request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "EXECUTION_STARTED", request.ExpectedIncidentRevision, request.Reason, async entity =>
        {
            if (await db.PlaybookExecutions.AnyAsync(e => e.OrganizationId == organization && e.IncidentId == incident && e.Status == "IN_PROGRESS", ct))
                throw Error(409, "incident_execution_already_active");
            var playbook = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PlaybookId &&
                (p.OrganizationId == null || p.OrganizationId == organization), ct) ?? throw Error(404, "execution_playbook_not_found");
            if (playbook.Revision != request.ExpectedPlaybookRevision) throw Error(409, "stale_playbook_revision");
            if (playbook.CurrentPublishedVersionId != request.VersionId) throw Error(409, "execution_version_not_current");
            var version = await db.PlaybookVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == request.VersionId && v.PlaybookId == playbook.Id, ct)
                ?? throw Error(404, "execution_version_not_found");
            if (await EffectiveHold(version.Id,null,ct) is not null) throw Error(409,"security_held_source");
            if (version.Status != "PUBLISHED" || version.PublishedAt is null) throw Error(409, "execution_version_not_published");
            if (version.CategoryId != entity.CategoryId || !await db.IncidentCategories.AnyAsync(c => c.Id == version.CategoryId && c.IsActive, ct))
                throw Error(409, "execution_category_mismatch_or_inactive");
            var steps = await db.PlaybookSteps.AsNoTracking().Where(s => s.VersionId == version.Id).ToListAsync(ct);
            if (steps.Count is < 1 or > 100) throw Error(409, "invalid_execution_steps");
            var codes = steps.ToDictionary(s => s.Id, s => s.Code);
            var dependencies = await db.PlaybookStepDependencies.AsNoTracking().Where(d => d.VersionId == version.Id).ToListAsync(ct);
            if (!PlaybookDependencyGraph.IsAcyclic(codes.Values, dependencies.Select(d => (codes[d.PredecessorId], codes[d.SuccessorId]))))
                throw Error(409, "invalid_execution_dependencies");
            if (await db.PlaybookCapabilityRequirements.AnyAsync(r => r.VersionId == version.Id
                && !db.CapabilityCatalog.Any(c => c.Id == r.CapabilityId && c.IsActive), ct))
                throw Error(409, "inactive_execution_capability_reference");
            var now = DateTimeOffset.UtcNow;
            var execution = new PlaybookExecution { OrganizationId = organization, IncidentId = incident, PlaybookId = playbook.Id,
                VersionId = version.Id, CreatedByUserId = actor, CreatedAt = now.AddTicks(-(now.Ticks % 10)) };
            db.PlaybookExecutions.Add(execution);
            db.ExecutionTasks.AddRange(steps.Select(s => new ExecutionTask { OrganizationId = organization, IncidentId = incident,
                ExecutionId = execution.Id, VersionId = version.Id, StepId = s.Id }));
            return (execution, null);
        }, ct);

    private async Task<PlaybookExecution> ActiveExecution(Guid organization, Guid incident, Guid id, long expected, CancellationToken ct)
    {
        var entity = await db.PlaybookExecutions.SingleOrDefaultAsync(e => e.OrganizationId == organization && e.IncidentId == incident && e.Id == id, ct)
            ?? throw Error(404, "execution_not_found");
        if (entity.Revision != expected || entity.Revision == long.MaxValue) throw Error(409, "stale_execution_revision");
        if (entity.Status != "IN_PROGRESS") throw Error(409, "execution_is_terminal");
        entity.Revision++;
        return entity;
    }
    private async Task<ExecutionTask> FindTask(PlaybookExecution execution, Guid task, CancellationToken ct) =>
        await db.ExecutionTasks.SingleOrDefaultAsync(t => t.ExecutionId == execution.Id && t.Id == task
            && t.OrganizationId == execution.OrganizationId && t.IncidentId == execution.IncidentId, ct) ?? throw Error(404, "execution_task_not_found");

    private async Task EligibleMembership(Guid organization, Guid membership, Guid? actor, CancellationToken ct)
    {
        if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.Id == membership
            && m.Status == MembershipStatus.Active && (m.Role == OrganizationRole.OrgAdmin || m.Role == OrganizationRole.Analyst)
            && (actor == null || m.UserId == actor) && db.Users.Any(u => u.Id == m.UserId && u.IsActive &&
                (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)), ct))
            throw Error(409, "active_assigned_task_writer_required");
    }
    private async Task RequireUsableExecution(PlaybookExecution execution, CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""SELECT app.execution_version_usable({execution.Id}) AS "Value" """).SingleAsync(ct))
            throw Error(409, "execution_version_withdrawn");
    }

    public Task<ExecutionItem> AssignAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        AssignExecutionTask request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "EXECUTION_TASK_ASSIGNED", request.ExpectedIncidentRevision, request.Reason, async _ =>
        {
            var entity = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            await RequireUsableExecution(entity, ct);
            var item = await FindTask(entity, task, ct);
            if (item.Status is "COMPLETED" or "SKIPPED") throw Error(409, "execution_task_is_terminal");
            if (item.Status == "IN_PROGRESS" && request.AssignedMembershipId is null) throw Error(409, "execution_task_assignee_required");
            if (request.AssignedMembershipId is Guid id) await EligibleMembership(organization, id, null, ct);
            if (item.Status == "IN_PROGRESS" && (await db.PlaybookSteps.AnyAsync(s => s.Id == item.StepId && s.RequiresApproval, ct)
                || await db.DependencyExceptions.AnyAsync(a => a.TaskId == item.Id && a.Action == "APPROVED", ct)))
                throw Error(409, "block_task_before_reassignment");
            item.AssignedMembershipId = request.AssignedMembershipId; item.ChangedAtRevision = entity.Revision;
            return (entity, item);
        }, ct);

    public Task<ExecutionItem> ChangeAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task,
        ChangeExecutionTask request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "EXECUTION_TASK_CHANGED", request.ExpectedIncidentRevision, request.Reason, async _ =>
        {
            var entity = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            await RequireUsableExecution(entity, ct);
            var item = await FindTask(entity, task, ct);
            if (item.AssignedMembershipId is not Guid membership) throw Error(409, "execution_task_unassigned");
            await EligibleMembership(organization, membership, actor, ct);
            if (!Enum.IsDefined(request.TargetState)) throw Error(400, "invalid_execution_task_state");
            var target = request.TargetState switch { ExecutionTaskState.InProgress => "IN_PROGRESS", _ => request.TargetState.ToString().ToUpperInvariant() };
            if (target == "SKIPPED") throw Error(409, "task_skip_authorization_not_available");
            var valid = (item.Status, target) switch
            {
                ("PENDING", "IN_PROGRESS" or "BLOCKED") => true,
                ("BLOCKED", "PENDING" or "IN_PROGRESS") => true,
                ("IN_PROGRESS", "BLOCKED" or "COMPLETED") => true,
                _ => false
            };
            if (!valid) throw Error(409, "invalid_execution_task_transition");
            if (target is "IN_PROGRESS" or "COMPLETED")
            {
                await RequireExecutionDependencies(entity, item, ct);
                var step = await db.PlaybookSteps.AsNoTracking().SingleAsync(s => s.Id == item.StepId && s.VersionId == entity.VersionId, ct);
                if (step.RequiresApproval && !await HasStepApproval(item, ct)) throw Error(409, "task_approval_required");
                if (target == "COMPLETED" && step.RequiresEvidence && request.CompletionEvidenceId is null) throw Error(409, "task_evidence_required");
            }
            if (request.CompletionEvidenceId is Guid evidence)
            {
                if (target != "COMPLETED") throw Error(400, "evidence_only_on_completion");
                if (!await db.IncidentEvidence.AnyAsync(e => e.Id == evidence && e.OrganizationId == organization && e.IncidentId == incident
                    && e.VersionId == entity.VersionId && e.ExecutionId == execution && e.TaskId == task && e.Status == "ACTIVE"
                    && (e.Kind != "FILE" || db.EvidenceFiles.Any(f => f.EvidenceId == e.Id && f.State == "AVAILABLE")), ct))
                    throw Error(409, "active_task_evidence_required");
                await VerifyCompletionFile(actor, organization, incident, evidence, ct);
                item.CompletionEvidenceId = evidence;
            }
            item.Status = target; item.ChangedAtRevision = entity.Revision;
            return (entity, item);
        }, ct);

    public Task<ExecutionItem> CompleteAsync(Guid actor, Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct = default) =>
        FinishExecution(actor, organization, incident, execution, request, false, ct);
    public Task<ExecutionItem> CancelAsync(Guid actor, Guid organization, Guid incident, Guid execution, ExecutionCommand request, CancellationToken ct = default) =>
        FinishExecution(actor, organization, incident, execution, request, true, ct);
    private Task<ExecutionItem> FinishExecution(Guid actor, Guid organization, Guid incident, Guid execution, ExecutionCommand request, bool cancel, CancellationToken ct) =>
        ExecutionWrite(actor, organization, incident, cancel ? "EXECUTION_CANCELLED" : "EXECUTION_COMPLETED", request.ExpectedIncidentRevision, request.Reason, async _ =>
        {
            var entity = await ActiveExecution(organization, incident, execution, request.ExpectedExecutionRevision, ct);
            if (cancel)
            {
                if (await EffectiveHold(entity.VersionId,null,ct) is not null) throw Error(409,"security_hold_decision_required");
                var withdrawn = !await db.PlaybookVersions.AnyAsync(v => v.Id == entity.VersionId && v.Status == "PUBLISHED", ct);
                if (withdrawn && !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                    && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct)) throw Error(403, "organization_admin_required");
            }
            else
            {
                await RequireUsableExecution(entity, ct);
                if (await db.ExecutionTasks.AnyAsync(t => t.ExecutionId == entity.Id && t.Status != "COMPLETED" && t.Status != "SKIPPED", ct))
                    throw Error(409, "execution_tasks_not_completed");
            }
            entity.Status = cancel ? "CANCELLED" : "COMPLETED"; entity.FinishReason = request.Reason.Trim();
            var now = DateTimeOffset.UtcNow; entity.FinishedAt = now.AddTicks(-(now.Ticks % 10));
            return (entity, null);
        }, ct);

    private async Task ValidateExecutionContextAsync(Incident entity, CancellationToken ct)
    {
        if (await db.PlaybookExecutions.AnyAsync(e => e.OrganizationId == entity.OrganizationId && e.IncidentId == entity.Id && e.Status == "IN_PROGRESS", ct)
            && (entity.Status != "IN_RESPONSE" || entity.CategoryId != db.Entry(entity).Property(e => e.CategoryId).OriginalValue))
            throw Error(409, "incident_has_active_execution");
    }

    private async Task<ExecutionItem> ExecutionView(PlaybookExecution entity, long incidentRevision, CancellationToken ct)
    {
        var tasks = await db.ExecutionTasks.Where(t => t.ExecutionId == entity.Id).ToListAsync(ct);
        tasks.AddRange(db.ChangeTracker.Entries<ExecutionTask>().Where(e => e.State == EntityState.Added && e.Entity.ExecutionId == entity.Id).Select(e => e.Entity));
        var steps = await db.PlaybookSteps.AsNoTracking().Where(s => s.VersionId == entity.VersionId).ToDictionaryAsync(s => s.Id, ct);
        var version = await db.PlaybookVersions.AsNoTracking().SingleAsync(v => v.Id == entity.VersionId, ct);
        var ids = tasks.ToDictionary(t => t.StepId, t => t.Id);
        var dependencies = await db.PlaybookStepDependencies.AsNoTracking().Where(d => d.VersionId == entity.VersionId).ToListAsync(ct);
        var requirements = await db.PlaybookCapabilityRequirements.AsNoTracking().Where(r => r.VersionId == entity.VersionId).OrderBy(r => r.Id)
            .Select(r => new ExecutionRequirementItem(r.CapabilityId, r.StepId, r.Level)).ToListAsync(ct);
        return new(entity.Id, entity.OrganizationId, entity.IncidentId, entity.PlaybookId, entity.VersionId, version.Title, version.VersionNumber,
            version.Status, entity.Status, entity.Revision, incidentRevision, entity.CreatedAt, entity.FinishedAt, entity.FinishReason,
            tasks.OrderBy(t => steps[t.StepId].Position).Select(t => { var s = steps[t.StepId]; return new ExecutionTaskItem(t.Id, s.Id, s.Code,
                s.Position, s.Phase, s.Instructions, s.IsRequired, s.RequiresApproval, s.RequiresEvidence, t.Status, t.AssignedMembershipId, t.ChangedAtRevision, t.CompletionEvidenceId); }).ToList(),
            dependencies.OrderBy(d => d.Id).Select(d => new ExecutionDependencyItem(ids[d.PredecessorId], ids[d.SuccessorId])).ToList(), requirements);
    }
}
