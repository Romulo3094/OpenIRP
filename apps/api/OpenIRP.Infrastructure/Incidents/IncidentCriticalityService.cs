using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentItem> AssessCriticalityAsync(Guid actor, Guid organization, Guid incident,
        AssessIncidentCriticality request, CancellationToken ct = default)
    {
        string details = "{}";
        return WriteAsync(actor, organization, incident, "CRITICALITY_ASSESSED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if ((request.AssessedCriticality.HasValue && !Enum.IsDefined(request.AssessedCriticality.Value))
                || request.ReevaluationJustification?.Length > 2000)
                throw Error(400, "invalid_incident_criticality");
            // The shared writer gate makes these sources coherent with the checked incident revision.
            // Stream small identifiers only: event details stay bounded even with many reported assets.
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long count = 0, unknown = 0, sourceRevision = 0;
            AssetCriticality? highest = null;
            var rows = db.IncidentAssets.AsNoTracking()
                .Where(a => a.OrganizationId == organization && a.IncidentId == incident).OrderBy(a => a.Id)
                .Select(a => new { a.Id, a.AssetId, a.ReportedCriticality, a.ReportedAtRevision, a.LinkedAtRevision });
            await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct))
            {
                count++;
                if (row.ReportedCriticality is null) unknown++;
                else if (highest is null || row.ReportedCriticality > highest) highest = row.ReportedCriticality;
                sourceRevision = Math.Max(sourceRevision, Math.Max(row.ReportedAtRevision, row.LinkedAtRevision ?? 0));
                var canonical = FormattableString.Invariant(
                    $"{row.Id:D}|{row.AssetId:D}|{row.ReportedCriticality}|{row.ReportedAtRevision}|{row.LinkedAtRevision}\n");
                digest.AppendData(Encoding.UTF8.GetBytes(canonical));
            }
            var value = request.AssessedCriticality;
            var lowersKnown = value.HasValue && highest.HasValue && value != IncidentCriticality.NotApplicable
                && (int)value.Value - 1 < (int)highest.Value;
            // Unknown/reset/invalidation must not erase the last known decision for a later reduction.
            var previousDecision = await db.IncidentEvents.AsNoTracking()
                .Where(e => e.OrganizationId == organization && e.IncidentId == incident
                    && e.Action == "CRITICALITY_ASSESSED" && e.AssessedCriticality != null)
                .OrderByDescending(e => e.Revision)
                .Select(e => new { e.AssessedCriticality, e.Revision }).FirstOrDefaultAsync(ct);
            var previous = previousDecision?.AssessedCriticality;
            var previousRevision = previousDecision?.Revision;
            var lowersPrevious = value.HasValue && previous is IncidentCriticality.Low or IncidentCriticality.Medium or IncidentCriticality.High
                && value.Value < previous.Value;
            if ((lowersKnown || lowersPrevious || (value == IncidentCriticality.NotApplicable && count > 0))
                && string.IsNullOrWhiteSpace(request.ReevaluationJustification))
                throw Error(400, "criticality_reevaluation_justification_required");
            entity.AssessedCriticality = value;
            entity.CriticalityAssessedAtRevision = entity.Revision;
            details = JsonSerializer.Serialize(new
            {
                criticalityAssessment = new
                {
                    value, assessmentRevision = entity.Revision,
                    previousAssessmentValue = previous, previousAssessmentRevision = previousRevision,
                    source = "IncidentAssetReports",
                    assetCount = count, unknownReportedCriticalityCount = unknown,
                    highestReportedCriticality = highest, assetContextRevision = sourceRevision,
                    sourceDigestVersion = 1, sourceDigestSha256 = Convert.ToHexString(digest.GetHashAndReset()),
                    reevaluationJustification = string.IsNullOrWhiteSpace(request.ReevaluationJustification) ? null : request.ReevaluationJustification.Trim()
                }
            }, DetailsOptions);
            return (entity, request.Reason.Trim());
        }, ct, () => details);
    }
}
