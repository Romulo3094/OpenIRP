namespace OpenIRP.Domain.Entities;

public sealed class PlaybookExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid PlaybookId { get; set; }
    public Guid VersionId { get; set; }
    public string Status { get; set; } = "IN_PROGRESS";
    public long Revision { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? FinishReason { get; set; }
}
public sealed class ExecutionTask
{
    public Guid? CompletionEvidenceId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid VersionId { get; set; }
    public Guid StepId { get; set; }
    public string Status { get; set; } = "PENDING";
    public Guid? AssignedMembershipId { get; set; }
    public long ChangedAtRevision { get; set; } = 1;
}
public sealed class ExecutionTaskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid VersionId { get; set; }
    public Guid? TaskId { get; set; }
    public long Revision { get; set; }
    public long IncidentRevision { get; set; }
    public string ExecutionStatus { get; set; } = "";
    public string? TaskStatus { get; set; }
    public Guid? AssignedMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
public enum ExecutionTaskState { Pending, InProgress, Blocked, Completed, Skipped }
