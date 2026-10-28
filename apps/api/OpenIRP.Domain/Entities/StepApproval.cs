namespace OpenIRP.Domain.Entities;

public sealed class StepApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid TaskId { get; set; }
    public Guid AssignedMembershipId { get; set; }
    public Guid? RequestId { get; set; }
    public string Action { get; set; } = "REQUESTED";
    public long ExecutionRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public bool SelfApproval { get; set; }
    public string? SelfApprovalJustification { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
