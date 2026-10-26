using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record RegisterEvidence([property: JsonRequired] EvidenceKind Kind,
    [property: JsonRequired] EvidenceClassification Classification, string Title, string Content,
    Guid? ExecutionId, Guid? TaskId, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record WithdrawEvidence([property: JsonRequired] long ExpectedEvidenceRevision, string Reason);
public sealed record EvidenceItem(Guid Id, Guid OrganizationId, Guid IncidentId, Guid? ExecutionId, Guid? TaskId,
    string Kind, string Classification, string Status, long Revision, string? Title, string? Content,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, Guid? WithdrawnByUserId, DateTimeOffset? WithdrawnAt, string? WithdrawalReason);
public sealed record EvidenceResult(EvidenceItem Evidence, long IncidentRevision);
public sealed record EvidencePage(IReadOnlyList<EvidenceItem> Items, int? NextSkip);
public sealed record EvidenceEventItem(Guid Id, long Revision, string Action, Guid ActorUserId, Guid CorrelationId, string Reason, DateTimeOffset OccurredAt);
public interface IEvidenceService
{
    Task<EvidenceResult> RegisterAsync(Guid actor, Guid organization, Guid incident, RegisterEvidence request, CancellationToken ct = default);
    Task<EvidenceResult> WithdrawAsync(Guid actor, Guid organization, Guid incident, Guid evidence, WithdrawEvidence request, CancellationToken ct = default);
    Task<EvidencePage> ListEvidenceAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<EvidenceItem> GetEvidenceAsync(Guid actor, Guid organization, Guid incident, Guid evidence, CancellationToken ct = default);
    Task<IReadOnlyList<EvidenceEventItem>> EvidenceEventsAsync(Guid actor, Guid organization, Guid incident, Guid evidence, long afterRevision = 0, int limit = 50, CancellationToken ct = default);
}
