namespace OpenIRP.Application.Executions;

public enum ExecutionDependencySatisfaction { Unsatisfied, PredecessorCompleted, ApprovedException }

public sealed record ExecutionDependencyReadinessItem(Guid DependencyId, Guid PredecessorTaskId,
    string PredecessorCode, string PredecessorStatus, ExecutionDependencySatisfaction Satisfaction,
    Guid? LatestRequestId, long? LatestRequestRevision, Guid? LatestDecisionId, string? LatestDecisionAction,
    Guid? RevocationId = null, DateTimeOffset? RevokedAt = null, string? RevocationReason = null);

public sealed record TaskDependencyReadiness(Guid ExecutionId, Guid VersionId, string VersionStatus,
    string ExecutionStatus, long ExecutionRevision, long IncidentRevision, string IncidentStatus,
    Guid TaskId, string TaskStatus, Guid? AssignedMembershipId, IReadOnlyList<ExecutionDependencyReadinessItem> Dependencies)
{
    public int DependencyCount => Dependencies.Count;
    public int UnsatisfiedCount => Dependencies.Count(d => d.Satisfaction == ExecutionDependencySatisfaction.Unsatisfied);
    public bool AllDependenciesSatisfied => UnsatisfiedCount == 0;
    // Dependency satisfaction alone never authorizes a task transition.
    public bool RequiresHumanReview => true;
}
