using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;

namespace OpenIRP.Application.Executions;

public sealed record ExecutionCapabilityRequirement(Guid RequirementId, Guid? StepId, Guid? TaskId, string? StepCode,
    string? TaskStatus, PlaybookRequirementLevel Level, CapabilityRequirementCoverage Coverage, IncidentCapabilityItem Capability)
{
    public bool HasNotesOrLimitations => !string.IsNullOrWhiteSpace(Capability.OrganizationNotes) || !string.IsNullOrWhiteSpace(Capability.Limitations);
}

public sealed record ExecutionCapabilityReadiness(Guid ExecutionId, Guid VersionId, string VersionStatus,
    string ExecutionStatus, long ExecutionRevision, long IncidentRevision, string IncidentStatus,
    Guid? TaskId, string? TaskStatus, RequiredCapabilityCoverage RequiredCoverage,
    IReadOnlyList<ExecutionCapabilityRequirement> Requirements)
{
    public bool RequiresHumanReview => true;
    public int RequiredCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Required);
    public int RequiredDeclaredAvailableCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Required && r.Coverage == CapabilityRequirementCoverage.DeclaredAvailable);
    public int RequiredUnknownCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Required && r.Coverage == CapabilityRequirementCoverage.Unknown);
    public int RequiredGapCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Required && r.Coverage is CapabilityRequirementCoverage.Unavailable or CapabilityRequirementCoverage.InactiveCatalog);
    public int RecommendedGapCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Recommended && r.Coverage is CapabilityRequirementCoverage.Unavailable or CapabilityRequirementCoverage.InactiveCatalog);
    public int RecommendedUnknownCount => Requirements.Count(r => r.Level == PlaybookRequirementLevel.Recommended && r.Coverage == CapabilityRequirementCoverage.Unknown);
    public bool HasNotesOrLimitations => Requirements.Any(r => r.HasNotesOrLimitations);
}
