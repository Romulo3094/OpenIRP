using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Severity;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentItem> TransitionAsync(Guid actor, Guid organization, Guid incident,
        TransitionIncident request, CancellationToken ct = default)
    {
        string? from = null;
        Guid? riskReviewId = null;
        return WriteAsync(actor, organization, incident, "STATE_CHANGED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason);
            if (request.ExpectedClosureContext is not null && request.TargetState != IncidentState.Closed)
                throw Error(400, "unexpected_closure_context");
            await CheckClosureContext(organization, incident, request.ExpectedClosureContext, ct);
            Next(entity, request.ExpectedRevision);
            if (!Enum.IsDefined(request.TargetState)) throw Error(400, "invalid_incident_state");
            from = entity.Status;
            if (!IncidentWorkflow.CanTransition(from, request.TargetState)) throw Error(409, "invalid_incident_transition");
            if (request.TargetState == IncidentState.Resolved)
            {
                if (string.IsNullOrWhiteSpace(request.Resolution) || request.Resolution.Length > 4000)
                    throw Error(400, "incident_resolution_required");
                entity.Resolution = request.Resolution.Trim();
            }
            else
            {
                if (request.Resolution is not null) throw Error(400, "unexpected_incident_resolution");
                if (request.TargetState == IncidentState.Closed)
                {
                    RequireClassifiedResolution(entity);
                    await RequireAssignedWriterAsync(actor, entity, ct);
                    if (entity.MaximumSeverityReached is IncidentSeverity.High or IncidentSeverity.Critical)
                        throw Error(409, "closure_approval_required");
                    riskReviewId = await RequireRiskAcceptance(entity, ct);
                }
                else entity.Resolution = null;
            }
            entity.Status = IncidentWorkflow.Store(request.TargetState);
            return (entity, request.Reason.Trim());
        }, ct, () => JsonSerializer.Serialize(new { workflow = new { from, to = IncidentWorkflow.Store(request.TargetState) }, closureContextToken = request.ExpectedClosureContext, riskReviewId }, DetailsOptions));
    }

    public Task<IncidentItem> RequestClosureAsync(Guid actor, Guid organization, Guid incident,
        RequestIncidentClosure request, CancellationToken ct = default)
    {
        Guid? riskReviewId = null;
        return WriteAsync(actor, organization, incident, "CLOSURE_REQUESTED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason);
            await CheckClosureContext(organization, incident, request.ExpectedClosureContext, ct);
            Next(entity, request.ExpectedRevision);
            if (entity.Status != "RESOLVED") throw Error(409, "incident_not_resolved");
            RequireClassifiedResolution(entity);
            await RequireAssignedWriterAsync(actor, entity, ct);
            if (entity.MaximumSeverityReached is not (IncidentSeverity.High or IncidentSeverity.Critical))
                throw Error(409, "closure_approval_not_required");
            if (entity.ClosureReviewStatus == ClosureReviewStatus.Pending)
                throw Error(409, "closure_request_already_pending");
            riskReviewId = await RequireRiskAcceptance(entity, ct);
            entity.ClosureRequestId = Guid.NewGuid();
            entity.ClosureRequestRevision = entity.Revision;
            entity.ClosureRequestedByUserId = actor;
            entity.ClosureReviewStatus = ClosureReviewStatus.Pending;
            entity.ClosureDecidedByUserId = null; entity.ClosureDecisionRevision = null;
            entity.ClosureSelfApproval = null; entity.ClosureSelfApprovalJustification = null;
            return (entity, request.Reason.Trim());
        }, ct, () => JsonSerializer.Serialize(new { closureContextToken = request.ExpectedClosureContext, riskReviewId }, DetailsOptions));
    }

    public Task<IncidentItem> DecideClosureAsync(Guid actor, Guid organization, Guid incident, Guid closureRequest,
        DecideIncidentClosure request, CancellationToken ct = default)
    {
        Guid? riskReviewId = null;
        return WriteAsync(actor, organization, incident, request.Approve ? "CLOSURE_APPROVED" : "CLOSURE_REJECTED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason);
            await CheckClosureContext(organization, incident, request.ExpectedClosureContext, ct);
            Next(entity, request.ExpectedRevision);
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct))
                throw Error(403, "organization_admin_required");
            if (entity.ClosureRequestId != closureRequest) throw Error(404, "closure_request_not_found");
            if (entity.Status != "RESOLVED" || entity.ClosureReviewStatus != ClosureReviewStatus.Pending
                || entity.ClosureRequestRevision != entity.Revision - 1)
                throw Error(409, "closure_request_not_current");
            var self = request.Approve && actor == entity.ClosureRequestedByUserId;
            if (request.SelfApproval != self || request.SelfApprovalJustification?.Length > 2000
                || (self && string.IsNullOrWhiteSpace(request.SelfApprovalJustification))
                || (!self && !string.IsNullOrWhiteSpace(request.SelfApprovalJustification)))
                throw Error(400, "invalid_self_approval_declaration");
            if (request.Approve)
            {
                RequireClassifiedResolution(entity);
                await RequireAssignedWriterAsync(entity.ClosureRequestedByUserId!.Value, entity, ct);
                if (entity.MaximumSeverityReached is not (IncidentSeverity.High or IncidentSeverity.Critical))
                    throw Error(409, "closure_approval_not_required");
                riskReviewId = await RequireRiskAcceptance(entity, ct);
                entity.Status = "CLOSED";
            }
            entity.ClosureReviewStatus = request.Approve ? ClosureReviewStatus.Approved : ClosureReviewStatus.Rejected;
            entity.ClosureDecidedByUserId = actor; entity.ClosureDecisionRevision = entity.Revision;
            entity.ClosureSelfApproval = self;
            entity.ClosureSelfApprovalJustification = self ? request.SelfApprovalJustification!.Trim() : null;
            return (entity, request.Reason.Trim());
        }, ct, () => JsonSerializer.Serialize(new { closureContextToken = request.ExpectedClosureContext, riskReviewId }, DetailsOptions));
    }

    private static void RequireClassifiedResolution(Incident entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Resolution)) throw Error(409, "incident_resolution_required");
        if (!ClassificationComplete(entity)) throw Error(409, "incident_classification_pending");
    }

    private static bool ClassificationComplete(Incident entity) =>
        entity.Severity is not null && entity.MaximumSeverityReached is not null && entity.SeverityRuleVersion == SeverityRuleV1.Version
        && entity.ConfidentialityImpact is not null && entity.IntegrityImpact is not null && entity.AvailabilityImpact is not null
        && entity.Scope is not null && entity.OperationalImpact is not null && entity.AssessedCriticality is not null;

    private async Task RequireAssignedWriterAsync(Guid actor, Incident entity, CancellationToken ct)
    {
        if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == entity.OrganizationId
            && m.Id == entity.AssignedMembershipId && m.UserId == actor && m.Status == MembershipStatus.Active
            && (m.Role == OrganizationRole.Analyst || m.Role == OrganizationRole.OrgAdmin)
            && db.Users.Any(u => u.Id == actor && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)), ct))
            throw Error(409, "active_assigned_incident_writer_required");
    }

    private void ValidateWorkflowMutation(Incident entity, string action, Guid actor)
    {
        var originalState = db.Entry(entity).Property(e => e.Status).OriginalValue;
        if (originalState is "CLOSED" or "DISCARDED" && action != "STATE_CHANGED")
            throw Error(409, "terminal_incident_requires_reopening");
        // Approval is consumed by closing in the same transaction. Every other write invalidates a pending review.
        if (action is not ("CLOSURE_REQUESTED" or "CLOSURE_APPROVED" or "CLOSURE_REJECTED")
            && entity.ClosureReviewStatus is ClosureReviewStatus.Pending or ClosureReviewStatus.Approved)
        {
            entity.ClosureReviewStatus = ClosureReviewStatus.Invalidated;
            entity.ClosureDecidedByUserId = actor;
            entity.ClosureDecisionRevision = entity.Revision;
            entity.ClosureSelfApproval = false;
            entity.ClosureSelfApprovalJustification = null;
        }
    }

    private static IncidentClosureReview? ClosureView(Guid? id, long? revision, Guid? requestedBy, ClosureReviewStatus? status,
        Guid? decidedBy, long? decisionRevision, bool? selfApproval, string? justification) =>
        id is null ? null : new(id.Value, revision!.Value, requestedBy!.Value, status!.Value, decidedBy, decisionRevision, selfApproval, justification);
}
