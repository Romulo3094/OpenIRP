using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIRP.Application.Executions;

public sealed record RequestSkipAuthorization(string Impact, string ResidualRisk,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record DecideSkipAuthorization([property: JsonRequired] bool Approve,
    [property: JsonRequired] bool SelfApproval, string? SelfApprovalJustification,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record SkipAuthorizationItem(Guid Id, Guid? RequestId, string Action, Guid AssignedMembershipId,
    long ExecutionRevision, long IncidentRevision, Guid ActorUserId, Guid CorrelationId, string Reason,
    string Impact, string ResidualRisk, JsonElement DependencyContext, bool SelfApproval,
    string? SelfApprovalJustification, DateTimeOffset OccurredAt);
