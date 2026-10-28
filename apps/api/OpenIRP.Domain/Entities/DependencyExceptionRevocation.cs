namespace OpenIRP.Domain.Entities;

public sealed class DependencyExceptionRevocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid TaskId { get; set; }
    public Guid DependencyId { get; set; }
    public Guid PredecessorTaskId { get; set; }
    public Guid SkipAuthorizationId { get; set; }
    public Guid AssignedMembershipId { get; set; }
    public Guid ApprovalId { get; set; }
    public long ExecutionRevision { get; set; }
    public long IncidentRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public bool BlockedTask { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
