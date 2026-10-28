using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private Task<string> RiskContext(Guid organization, Guid incident, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"""SELECT app.closure_risk_context({organization},{incident}) AS "Value" """).SingleAsync(ct);

    private static ClosureRiskReviewItem RiskView(ClosureRiskReview r) => new(r.Id, r.IncidentId, r.IncidentRevision,
        r.ActorUserId, r.CorrelationId, r.ContextToken, r.AcceptResidualRisk, r.Assessment, r.Reason, r.SelfReview, r.SelfReviewJustification, r.OccurredAt);

    private async Task<ClosureRiskState> RiskState(Guid organization, Guid incident, bool resolved, CancellationToken ct)
    {
        var token = await RiskContext(organization, incident, ct);
        var latest = await db.ClosureRiskReviews.AsNoTracking().Where(r => r.OrganizationId == organization && r.IncidentId == incident)
            .OrderByDescending(r => r.IncidentRevision).FirstOrDefaultAsync(ct);
        var current = resolved && latest is not null && latest.ContextToken == token && await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == organization && m.UserId == latest.ActorUserId && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin
            && db.Users.Any(u => u.Id == m.UserId && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)), ct);
        return new(token, latest is null ? null : RiskView(latest), current);
    }

    private async Task<Guid> RequireRiskAcceptance(Incident entity, CancellationToken ct)
    {
        var state = await RiskState(entity.OrganizationId, entity.Id, entity.Status == "RESOLVED", ct);
        if (!state.AcceptanceCurrent) throw Error(409, "current_closure_risk_acceptance_required");
        return state.LatestReview!.Id;
    }

    public async Task<ClosureRiskReviewItem> ReviewClosureRiskAsync(Guid actor, Guid organization, Guid incident,
        ReviewClosureRisk request, CancellationToken ct = default)
    {
        ClosureRiskReview? review = null;
        await WriteAsync(actor, organization, incident, "CLOSURE_RISK_REVIEWED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason);
            if (request.Reason.Contains('\0') || request.SelfReviewJustification?.Contains('\0') == true)
                throw Error(400, "invalid_risk_review_text");
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Role == OrganizationRole.OrgAdmin && m.Status == MembershipStatus.Active, ct)) throw Error(403, "organization_admin_required");
            if (entity.Status != "RESOLVED") throw Error(409, "incident_not_resolved");
            RequireClassifiedResolution(entity);
            if (string.IsNullOrWhiteSpace(request.Assessment) || request.Assessment.Length > 4000 || request.Assessment.Contains('\0'))
                throw Error(400, "closure_risk_assessment_required");
            if (request.ExpectedClosureContext is null) throw Error(400, "closure_context_required");
            await CheckClosureContext(organization, incident, request.ExpectedClosureContext, ct);
            Next(entity, request.ExpectedIncidentRevision);
            var self = await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.Id == entity.AssignedMembershipId && m.UserId == actor, ct);
            if (request.SelfReview != self || request.SelfReviewJustification?.Length > 2000
                || (self && string.IsNullOrWhiteSpace(request.SelfReviewJustification))
                || (!self && request.SelfReviewJustification is not null)) throw Error(400, "invalid_risk_self_review_declaration");
            review = new() { OrganizationId = organization, IncidentId = incident, IncidentRevision = entity.Revision,
                ActorUserId = actor, CorrelationId = context.CorrelationId, ContextToken = await RiskContext(organization, incident, ct),
                AcceptResidualRisk = request.AcceptResidualRisk, Assessment = request.Assessment.Trim(), Reason = request.Reason.Trim(),
                SelfReview = self, SelfReviewJustification = self ? request.SelfReviewJustification!.Trim() : null, OccurredAt = EvidenceNow() };
            db.ClosureRiskReviews.Add(review);
            return (entity, request.Reason.Trim());
        }, ct, () => JsonSerializer.Serialize(new { riskReviewId = review!.Id, review.AcceptResidualRisk, review.ContextToken, closureContextToken = request.ExpectedClosureContext }, DetailsOptions));
        return RiskView(review!);
    }

    public Task<IReadOnlyList<ClosureRiskReviewItem>> ClosureRiskHistoryAsync(Guid actor, Guid organization, Guid incident,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        ExecutionRead<IReadOnlyList<ClosureRiskReviewItem>>(actor, organization, incident, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_incident_pagination");
            var rows = await db.ClosureRiskReviews.AsNoTracking().Where(r => r.OrganizationId == organization && r.IncidentId == incident && r.IncidentRevision > afterRevision)
                .OrderBy(r => r.IncidentRevision).Take(limit).ToListAsync(ct);
            return rows.Select(RiskView).ToList();
        }, ct);
}
