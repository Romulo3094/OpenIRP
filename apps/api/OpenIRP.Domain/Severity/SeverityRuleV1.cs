using OpenIRP.Domain.Entities;

namespace OpenIRP.Domain.Severity;

public sealed record SeverityInputs(ImpactLevel? Confidentiality, ImpactLevel? Integrity, ImpactLevel? Availability,
    IncidentCriticality? Criticality, IncidentScope? Scope, OperationalImpact? OperationalImpact);
public sealed record SeverityResult(string RuleVersion, IncidentSeverity? Severity, string MatchedRule)
{
    public string ClassificationStatus => Severity is null ? "Pending" : "Classified";
}

// A deterministic implementation of the documented OpenIRP matrix; adoption is a separate policy decision.
public static class SeverityRuleV1
{
    public const string Version = "openirp-severity-v1";

    public static SeverityResult Evaluate(SeverityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Validate(inputs.Confidentiality); Validate(inputs.Integrity); Validate(inputs.Availability);
        Validate(inputs.Criticality); Validate(inputs.Scope); Validate(inputs.OperationalImpact);
        if (inputs.Confidentiality is null || inputs.Integrity is null || inputs.Availability is null
            || inputs.Criticality is null || inputs.Scope is null || inputs.OperationalImpact is null)
            return new(Version, null, "INCOMPLETE_INPUTS");
        var cia = (ImpactLevel)Math.Max((int)inputs.Confidentiality.Value,
            Math.Max((int)inputs.Integrity.Value, (int)inputs.Availability.Value));
        var criticalAsset = inputs.Criticality == IncidentCriticality.High;
        var organizationWide = inputs.Scope == IncidentScope.OrganizationWide;
        var operation = inputs.OperationalImpact.Value;
        if (operation == OperationalImpact.CriticalInterruption || (cia == ImpactLevel.High && (criticalAsset || organizationWide)))
            return new(Version, IncidentSeverity.Critical, "R1_CRITICAL");
        if (cia == ImpactLevel.High || operation == OperationalImpact.MajorInterruption
            || (cia >= ImpactLevel.Medium && (criticalAsset || organizationWide)))
            return new(Version, IncidentSeverity.High, "R2_HIGH");
        if (cia >= ImpactLevel.Medium || operation == OperationalImpact.Degraded
            || (cia >= ImpactLevel.Low && (criticalAsset || inputs.Scope is IncidentScope.Multiple or IncidentScope.OrganizationWide)))
            return new(Version, IncidentSeverity.Medium, "R3_MEDIUM");
        return new(Version, IncidentSeverity.Low, "R4_LOW");
    }

    public static IncidentSeverity? Maximum(IncidentSeverity? previous, IncidentSeverity? current)
    {
        Validate(previous); Validate(current);
        if (current is null) return previous;
        return previous is null || current.Value > previous.Value ? current : previous;
    }

    private static void Validate<T>(T? value) where T : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown severity input or level.");
    }
}
