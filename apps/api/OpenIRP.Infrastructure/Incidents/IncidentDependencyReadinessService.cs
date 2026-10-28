using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<TaskDependencyReadiness> DependencyReadinessAsync(Guid actor, Guid organization, Guid incident,
        Guid execution, Guid task, CancellationToken ct = default) =>
        ExecutionRead(actor, organization, incident, async () =>
        {
            var run = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution &&
                e.OrganizationId == organization && e.IncidentId == incident, ct) ?? throw Error(404, "execution_not_found");
            var target = await FindTask(run, task, ct);
            var parent = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident && i.OrganizationId == organization, ct);
            var version = await db.PlaybookVersions.AsNoTracking().SingleAsync(v => v.Id == run.VersionId && v.PlaybookId == run.PlaybookId, ct);
            // One batch; the same SQL predicate used by task transitions evaluates every skipped edge.
            // Historical request/decision metadata is deliberately separate from current satisfaction.
            var rows = await db.Database.SqlQuery<DependencyReadinessRow>($"""
                WITH snapshot AS MATERIALIZED (
                    SELECT app.dependency_exception_context({execution}, {task}) AS value
                )
                SELECT d.id AS dependency_id, p.id AS predecessor_task_id, s.code AS predecessor_code,
                    p.status AS predecessor_status, s.position AS predecessor_position,
                    r.id AS latest_request_id, r.execution_revision AS latest_request_revision,
                    decision.id AS latest_decision_id, decision.action AS latest_decision_action,
                    revocation.id AS revocation_id, revocation.occurred_at AS revoked_at, revocation.reason AS revocation_reason,
                    CASE WHEN p.status='SKIPPED' THEN
                        app.has_dependency_exception(t.id,p.id,t.assigned_membership_id,snapshot.value)
                        ELSE false END AS exception_effective
                FROM app.execution_tasks t
                JOIN app.playbook_step_dependencies d ON d.version_id=t.version_id AND d.successor_id=t.step_id
                JOIN app.execution_tasks p ON p.execution_id=t.execution_id AND p.version_id=t.version_id
                    AND p.organization_id=t.organization_id AND p.incident_id=t.incident_id AND p.step_id=d.predecessor_id
                JOIN app.playbook_steps s ON s.id=p.step_id AND s.version_id=t.version_id
                CROSS JOIN snapshot
                LEFT JOIN LATERAL (
                    SELECT a.id,a.execution_revision FROM app.dependency_exceptions a
                    WHERE a.organization_id=t.organization_id AND a.incident_id=t.incident_id AND a.execution_id=t.execution_id
                        AND a.task_id=t.id AND a.predecessor_task_id=p.id AND a.dependency_id=d.id AND a.action='REQUESTED'
                    ORDER BY a.execution_revision DESC LIMIT 1
                ) r ON true
                LEFT JOIN app.dependency_exceptions decision ON decision.request_id=r.id
                LEFT JOIN app.dependency_exception_revocations revocation ON revocation.approval_id=decision.id
                WHERE t.id={task} AND t.execution_id={execution} AND t.organization_id={organization} AND t.incident_id={incident}
                ORDER BY s.position,p.id
                """).ToListAsync(ct);
            var dependencies = rows.Select(r => new ExecutionDependencyReadinessItem(r.DependencyId, r.PredecessorTaskId,
                r.PredecessorCode, r.PredecessorStatus,
                r.PredecessorStatus == "COMPLETED" ? ExecutionDependencySatisfaction.PredecessorCompleted :
                r.ExceptionEffective ? ExecutionDependencySatisfaction.ApprovedException : ExecutionDependencySatisfaction.Unsatisfied,
                r.LatestRequestId, r.LatestRequestRevision, r.LatestDecisionId, r.LatestDecisionAction, r.RevocationId, r.RevokedAt, r.RevocationReason)).ToList();
            return new TaskDependencyReadiness(run.Id, run.VersionId, version.Status, run.Status, run.Revision,
                parent.Revision, parent.Status, target.Id, target.Status, target.AssignedMembershipId, dependencies);
        }, ct);

    private sealed class DependencyReadinessRow
    {
        public Guid DependencyId { get; set; }
        public Guid PredecessorTaskId { get; set; }
        public string PredecessorCode { get; set; } = "";
        public string PredecessorStatus { get; set; } = "";
        public int PredecessorPosition { get; set; }
        public Guid? LatestRequestId { get; set; }
        public long? LatestRequestRevision { get; set; }
        public Guid? LatestDecisionId { get; set; }
        public string? LatestDecisionAction { get; set; }
        public Guid? RevocationId { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? RevocationReason { get; set; }
        public bool ExceptionEffective { get; set; }
    }
}
