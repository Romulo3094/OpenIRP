namespace OpenIRP.Application.Executions;

public sealed record DependencyRevocationItem(Guid Id, Guid ApprovalId, Guid TaskId, Guid PredecessorTaskId,
    long ExecutionRevision, long IncidentRevision, Guid ActorUserId, Guid CorrelationId, string Reason,
    bool BlockedTask, DateTimeOffset OccurredAt);
