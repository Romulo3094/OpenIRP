namespace OpenIRP.Domain.Entities;

public sealed class OperationalReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid TaskId { get; set; }
    public Guid EvidenceId { get; set; }
    public string Kind { get; set; } = "EVIDENCE";
    public bool Accepted { get; set; }
    public string Method { get; set; } = "";
    public string Assessment { get; set; } = "";
    public string? AlternativeProcedure { get; set; }
    public string ContextToken { get; set; } = "";
    public long IncidentRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Reason { get; set; } = "";
    public bool SelfReview { get; set; }
    public string? SelfReviewJustification { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
