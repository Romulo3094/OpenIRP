using System.Text.Json;
using System.Text.Json.Nodes;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Severity;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentItem> AssessSeverityAsync(Guid actor, Guid organization, Guid incident,
        AssessIncidentSeverity request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, incident, "SEVERITY_ASSESSED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            return (entity, request.Reason.Trim());
        }, ct);

    private static string CalculateSeverity(Incident entity, string action, string details)
    {
        var explicitCalculation = action is "REGISTERED" or "SEVERITY_ASSESSED";
        var changedInput = action is "IMPACT_ASSESSED" or "CONTEXT_ASSESSED" or "CRITICALITY_ASSESSED"
            or "ASSET_REPORTED" or "ASSET_ATTACHED" or "ASSET_LINKED";
        // Existing incidents need an explicit first calculation. Ordinary edits never invent prior classifications.
        if (!explicitCalculation && (entity.SeverityRuleVersion is null || !changedInput)) return details;
        if (entity.SeverityRuleVersion is not null && entity.SeverityRuleVersion != SeverityRuleV1.Version)
            throw Error(409, "unsupported_severity_rule_version");
        var inputs = new SeverityInputs(entity.ConfidentialityImpact, entity.IntegrityImpact, entity.AvailabilityImpact,
            entity.AssessedCriticality, entity.Scope, entity.OperationalImpact);
        var result = SeverityRuleV1.Evaluate(inputs);
        var previousMaximum = entity.MaximumSeverityReached;
        entity.Severity = result.Severity;
        entity.MaximumSeverityReached = SeverityRuleV1.Maximum(previousMaximum, result.Severity);
        entity.SeverityRuleVersion = result.RuleVersion;
        entity.SeverityAssessedAtRevision = entity.Revision;
        var payload = JsonNode.Parse(details) as JsonObject ?? throw new InvalidOperationException("Incident details must be an object.");
        payload["classification"] = JsonSerializer.SerializeToNode(new
        {
            inputs, entity.CriticalityAssessedAtRevision, result.RuleVersion, result.ClassificationStatus,
            result.MatchedRule, result.Severity, previousMaximum, entity.MaximumSeverityReached,
            assessedAtRevision = entity.Revision
        }, DetailsOptions);
        return payload.ToJsonString(DetailsOptions);
    }
}
