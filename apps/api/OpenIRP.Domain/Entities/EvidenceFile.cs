namespace OpenIRP.Domain.Entities;

public sealed class EvidenceFile
{
    public Guid EvidenceId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid StorageId { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public string State { get; set; } = "PENDING";
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? FailureCode { get; set; }
}
public sealed class EvidenceFileEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid EvidenceId { get; set; }
    public long Revision { get; set; }
    public string State { get; set; } = "";
    public string? FailureCode { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
