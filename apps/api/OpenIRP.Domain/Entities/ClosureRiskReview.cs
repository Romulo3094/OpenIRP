namespace OpenIRP.Domain.Entities;

public sealed class ClosureRiskReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public long IncidentRevision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string ContextToken { get; set; } = "";
    public bool AcceptResidualRisk { get; set; }
    public string Assessment { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool SelfReview { get; set; }
    public string? SelfReviewJustification { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
