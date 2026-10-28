using System.Text.Json.Serialization;
namespace OpenIRP.Application.Executions;

public sealed record DecideSecurityHold([property: JsonRequired] bool Continue,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record SecurityHoldItem(Guid Id, Guid VersionId, long PlaybookRevision, Guid ActorUserId, string Reason, DateTimeOffset OccurredAt, bool IsInherited = false);
public sealed record SecurityDecisionItem(Guid Id, Guid SecurityHoldId, string Action, long ExecutionRevision,
    long IncidentRevision, Guid ActorUserId, Guid CorrelationId, string Reason, DateTimeOffset OccurredAt);
public sealed record ExecutionSecurityState(Guid ExecutionId, Guid VersionId, string ExecutionStatus, string VersionStatus,
    long ExecutionRevision, long IncidentRevision, SecurityHoldItem? CurrentHold, IReadOnlyList<SecurityDecisionItem> Decisions,
    bool ContinuityAuthorized);
