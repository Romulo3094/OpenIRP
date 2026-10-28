namespace OpenIRP.Domain.Entities;

public sealed class PlaybookSecurityHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaybookId { get; set; }
    public Guid VersionId { get; set; }
    public long Revision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ExecutionSecurityDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid SecurityHoldId { get; set; }
    public string Action { get; set; } = "CONTINUE";
    public long ExecutionRevision { get; set; }
    public long IncidentRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
