using System.Text.Json.Serialization;

namespace OpenIRP.Application.Executions;

public sealed record DecideStepApproval([property: JsonRequired] bool Approve,
    [property: JsonRequired] bool SelfApproval, string? SelfApprovalJustification,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record StepApprovalItem(Guid Id, Guid? RequestId, string Action, Guid AssignedMembershipId,
    long ExecutionRevision, Guid ActorUserId, Guid CorrelationId, string Reason, bool SelfApproval,
    string? SelfApprovalJustification, DateTimeOffset OccurredAt);
