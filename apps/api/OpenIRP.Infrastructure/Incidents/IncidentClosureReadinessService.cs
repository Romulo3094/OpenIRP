using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentClosureReadiness> ClosureReadinessAsync(Guid actor, Guid organization, Guid incident,
        CancellationToken ct = default) => ExecutionRead(actor, organization, incident, async () =>
    {
        var entity = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident && i.OrganizationId == organization, ct);
        var now = DateTimeOffset.UtcNow;
        var members = db.OrganizationMemberships.AsNoTracking().Where(m => m.OrganizationId == organization
            && m.Status == MembershipStatus.Active && (m.Role == OrganizationRole.Analyst || m.Role == OrganizationRole.OrgAdmin)
            && db.Users.Any(u => u.Id == m.UserId && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= now)));
        var assigned = await members.Where(m => m.Id == entity.AssignedMembershipId)
            .Select(m => (Guid?)m.UserId).SingleOrDefaultAsync(ct);
        var isAdmin = await members.AnyAsync(m => m.UserId == actor && m.Role == OrganizationRole.OrgAdmin, ct);
        var runs = db.PlaybookExecutions.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident);
        var executions = await runs.GroupBy(e => e.Status).OrderBy(g => g.Key).Select(g => new ClosureStatusCount(g.Key, g.Count())).ToListAsync(ct);
        var tasks = await db.ExecutionTasks.AsNoTracking().Where(t => t.OrganizationId == organization && t.IncidentId == incident)
            .GroupBy(t => t.Status).OrderBy(g => g.Key).Select(g => new ClosureStatusCount(g.Key, g.Count())).ToListAsync(ct);
        var evidenceQuery = db.IncidentEvidence.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident);
        var evidence = await evidenceQuery.GroupBy(e => e.Status).OrderBy(g => g.Key).Select(g => new ClosureStatusCount(g.Key, g.Count())).ToListAsync(ct);
        var unavailableFiles = await evidenceQuery.CountAsync(e => e.Kind == "FILE" && e.Status == "ACTIVE"
            && !db.EvidenceFiles.Any(f => f.OrganizationId == organization && f.IncidentId == incident && f.EvidenceId == e.Id && f.State == "AVAILABLE"), ct);
        // Historical counts do not claim that an approval is currently usable or technically sufficient.
        var approvals = await db.DependencyExceptions.CountAsync(e => e.OrganizationId == organization && e.IncidentId == incident && e.Action == "APPROVED", ct);
        var revocations = await db.DependencyExceptionRevocations.CountAsync(e => e.OrganizationId == organization && e.IncidentId == incident, ct);
        var active = await runs.SingleOrDefaultAsync(e => e.Status == "IN_PROGRESS", ct);
        Guid? holdId = null;
        var continuity = false;
        if (active is not null)
        {
            holdId = (await EffectiveHold(active.VersionId,null,ct))?.Id;
            if (holdId is not null)
            {
                var decisions = await db.ExecutionSecurityDecisions.Where(d => d.OrganizationId == organization && d.IncidentId == incident
                    && d.ExecutionId == active.Id && d.SecurityHoldId == holdId).Select(d => d.Action).ToListAsync(ct);
                continuity = decisions.Contains("CONTINUE") && !decisions.Contains("CANCEL");
            }
        }
        var risk = await RiskState(organization, incident, entity.Status == "RESOLVED", ct);
        var blockers = new List<string>();
        if (!risk.AcceptanceCurrent) blockers.Add("current_closure_risk_acceptance_required");
        if (entity.Status != "RESOLVED") blockers.Add("incident_not_resolved");
        if (string.IsNullOrWhiteSpace(entity.Resolution)) blockers.Add("incident_resolution_required");
        if (!ClassificationComplete(entity)) blockers.Add("incident_classification_pending");
        if (assigned is null) blockers.Add("active_assigned_incident_writer_required");
        if (active is not null) blockers.Add("incident_has_active_execution");
        var approvalRequired = entity.MaximumSeverityReached is IncidentSeverity.High or IncidentSeverity.Critical;
        var currentRequest = entity.Status == "RESOLVED" && entity.ClosureReviewStatus == ClosureReviewStatus.Pending
            && entity.ClosureRequestId is not null && entity.ClosureRequestRevision == entity.Revision;
        if (entity.Revision == long.MaxValue) blockers.Add("stale_incident_revision");
        var ready = blockers.Count == 0;
        var assignedActor = assigned == actor;
        var requesterEligible = assigned is not null && assigned == entity.ClosureRequestedByUserId;
        return new IncidentClosureReadiness(incident, organization, entity.Revision, entity.Status, ClassificationComplete(entity),
            !string.IsNullOrWhiteSpace(entity.Resolution), assigned is not null, approvalRequired,
            ClosureView(entity.ClosureRequestId, entity.ClosureRequestRevision, entity.ClosureRequestedByUserId, entity.ClosureReviewStatus,
                entity.ClosureDecidedByUserId, entity.ClosureDecisionRevision, entity.ClosureSelfApproval, entity.ClosureSelfApprovalJustification),
            currentRequest, ready && assignedActor && !approvalRequired,
            ready && assignedActor && approvalRequired && entity.ClosureReviewStatus != ClosureReviewStatus.Pending,
            ready && isAdmin && approvalRequired && currentRequest && requesterEligible,
            ready && isAdmin && approvalRequired && currentRequest && requesterEligible && entity.ClosureRequestedByUserId == actor,
            blockers, executions, tasks, evidence, unavailableFiles, approvals, revocations, active?.Id, active?.Revision, holdId, continuity, await BuildClosureContext(organization, incident, now, ct), risk);
    }, ct);
}
