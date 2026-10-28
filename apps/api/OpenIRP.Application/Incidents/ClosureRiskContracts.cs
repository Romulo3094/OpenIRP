using System.Text.Json.Serialization;

namespace OpenIRP.Application.Incidents;

public sealed record ReviewClosureRisk([property: JsonRequired] bool AcceptResidualRisk, string Assessment, string Reason,
    [property: JsonRequired] bool SelfReview, string? SelfReviewJustification,
    [property: JsonRequired] long ExpectedIncidentRevision, [property: JsonRequired] string ExpectedClosureContext);
public sealed record ClosureRiskReviewItem(Guid Id, Guid IncidentId, long IncidentRevision, Guid ActorUserId,
    Guid CorrelationId, string ContextToken, bool AcceptResidualRisk, string Assessment, string Reason,
    bool SelfReview, string? SelfReviewJustification, DateTimeOffset OccurredAt);
public sealed record ClosureRiskState(string ContextToken, ClosureRiskReviewItem? LatestReview, bool IsCurrent)
{
    public bool AcceptanceCurrent => IsCurrent && LatestReview?.AcceptResidualRisk == true;
}
