namespace OpenIRP.Application.Executions;

public enum TaskEvidenceSupportState
{
    AccessRestricted, NotRequired, AwaitingCompletion, SkippedNeedsReview, MissingCompletionReference,
    InvalidCompletionReference, Withdrawn, FileUnavailable, MetadataAvailable
}
public enum RequiredEvidenceCoverage { NotRequired, MetadataAvailable, NeedsReview }

public sealed record TaskEvidenceSupport(Guid TaskId, Guid StepId, string StepCode, int Position, string TaskStatus,
    bool IsRequired, bool RequiresEvidence, Guid? CompletionEvidenceId, string? EvidenceKind, long? EvidenceRevision,
    string? EvidenceStatus, long? FileRevision, string? FileState, TaskEvidenceSupportState SupportState);

public sealed record ExecutionEvidenceReadiness(Guid ExecutionId, Guid VersionId, string VersionStatus, string ExecutionStatus,
    long ExecutionRevision, long IncidentRevision, string IncidentStatus, string RiskContextToken,
    IReadOnlyList<TaskEvidenceSupport> Tasks)
{
    public bool RequiresHumanReview => true;
    public bool FileIntegrityVerified => false;
    public bool TechnicalSufficiencyVerified => false;
    public int TaskCount => Tasks.Count;
    public int EvidenceRequiredTaskCount => Tasks.Count(t => t.RequiresEvidence);
    public int RequiredMetadataAvailableCount => Tasks.Count(t => t.RequiresEvidence && t.SupportState == TaskEvidenceSupportState.MetadataAvailable);
    public int CompletedRequiredEvidenceGapCount => Tasks.Count(t => t.RequiresEvidence && t.TaskStatus == "COMPLETED" && t.SupportState != TaskEvidenceSupportState.MetadataAvailable);
    public int CurrentReferencedEvidenceGapCount => Tasks.Count(t => t.CompletionEvidenceId is not null && t.SupportState != TaskEvidenceSupportState.MetadataAvailable);
    public int SkippedRequiringEvidenceCount => Tasks.Count(t => t.RequiresEvidence && t.TaskStatus == "SKIPPED");
    public int AwaitingRequiredCompletionCount => Tasks.Count(t => t.RequiresEvidence && t.SupportState == TaskEvidenceSupportState.AwaitingCompletion);
    public RequiredEvidenceCoverage RequiredCoverage => EvidenceRequiredTaskCount == 0 ? RequiredEvidenceCoverage.NotRequired
        : RequiredMetadataAvailableCount == EvidenceRequiredTaskCount ? RequiredEvidenceCoverage.MetadataAvailable : RequiredEvidenceCoverage.NeedsReview;
}
