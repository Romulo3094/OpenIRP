namespace OpenIRP.Domain.Entities;

public enum EvidenceKind { Text, Reference, File }
public enum EvidenceClassification { Internal, Confidential, Restricted }

public sealed class IncidentEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid? ExecutionId { get; set; }
    public Guid? TaskId { get; set; }
    public string Kind { get; set; } = "TEXT";
    public string Classification { get; set; } = "INTERNAL";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
    public long Revision { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? WithdrawnByUserId { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public string? WithdrawalReason { get; set; }
}

public sealed class EvidenceEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid EvidenceId { get; set; }
    public long Revision { get; set; }
    public string Action { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
