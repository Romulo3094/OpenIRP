using System.Text.Json.Serialization;
namespace OpenIRP.Application.Executions;

public enum OperationalReviewKind { Evidence, Alternative }
public sealed record RegisterOperationalReview([property: JsonRequired] OperationalReviewKind Kind,
    [property: JsonRequired] Guid EvidenceId, [property: JsonRequired] bool Accepted,
    string Method, string Assessment, string? AlternativeProcedure, string Reason,
    [property: JsonRequired] bool SelfReview, string? SelfReviewJustification,
    [property: JsonRequired] long ExpectedIncidentRevision, string ExpectedContext);
public sealed record OperationalReviewItem(Guid Id, Guid TaskId, Guid EvidenceId, string Kind, bool Accepted,
    string Method, string Assessment, string? AlternativeProcedure, string ContextToken,
    long IncidentRevision, Guid ActorUserId, Guid CorrelationId, string Reason,
    bool SelfReview, string? SelfReviewJustification, DateTimeOffset OccurredAt, bool IsCurrent)
{
    public bool AcceptanceCurrent => Accepted && IsCurrent;
}
public sealed record OperationalReviewPage(string ContextToken, IReadOnlyList<OperationalReviewItem> Items, long? NextRevision)
{
    public bool RequiresHumanReview => true;
    public bool AuthorizesExecution => false;
}
