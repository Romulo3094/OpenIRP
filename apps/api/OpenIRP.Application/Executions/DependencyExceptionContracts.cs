using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIRP.Application.Executions;

public sealed record RequestDependencyException(string Impact, string ResidualRisk,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record DecideDependencyException([property: JsonRequired] bool Approve,
    [property: JsonRequired] bool SelfApproval, string? SelfApprovalJustification,
    [property: JsonRequired] long ExpectedExecutionRevision, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record DependencyExceptionItem(Guid Id, Guid? RequestId, string Action, Guid DependencyId, Guid PredecessorTaskId, Guid SkipAuthorizationId, Guid AssignedMembershipId,
    long ExecutionRevision, long IncidentRevision, Guid ActorUserId, Guid CorrelationId, string Reason,
    string Impact, string ResidualRisk, JsonElement DependencyContext, bool SelfApproval,
    string? SelfApprovalJustification, DateTimeOffset OccurredAt);
