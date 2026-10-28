using OpenIRP.Domain.Entities;

namespace OpenIRP.Domain.Playbooks;

// These describe declared coverage, never permission or technical adequacy of an action.
public enum CapabilityRequirementCoverage { DeclaredAvailable, Unavailable, Unknown, InactiveCatalog }
public enum RequiredCapabilityCoverage { NoRequiredCapabilities, DeclaredAvailable, Unknown, Gaps }

public static class CapabilityRequirementEvaluation
{
    public static CapabilityRequirementCoverage Evaluate(bool catalogActive, CapabilityAvailability availability)
    {
        if (!Enum.IsDefined(availability)) throw new ArgumentOutOfRangeException(nameof(availability));
        if (!catalogActive) return CapabilityRequirementCoverage.InactiveCatalog;
        return availability switch
        {
            CapabilityAvailability.Available => CapabilityRequirementCoverage.DeclaredAvailable,
            CapabilityAvailability.Unavailable => CapabilityRequirementCoverage.Unavailable,
            _ => CapabilityRequirementCoverage.Unknown
        };
    }

    public static RequiredCapabilityCoverage Summarize(IEnumerable<CapabilityRequirementCoverage> required)
    {
        var values = required.ToArray();
        if (values.Any(v => !Enum.IsDefined(v))) throw new ArgumentOutOfRangeException(nameof(required));
        if (values.Length == 0) return RequiredCapabilityCoverage.NoRequiredCapabilities;
        if (values.Any(v => v is CapabilityRequirementCoverage.Unavailable or CapabilityRequirementCoverage.InactiveCatalog))
            return RequiredCapabilityCoverage.Gaps;
        return values.Contains(CapabilityRequirementCoverage.Unknown) ? RequiredCapabilityCoverage.Unknown : RequiredCapabilityCoverage.DeclaredAvailable;
    }
}
