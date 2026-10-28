using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record TransitionIncident([property: JsonRequired] IncidentState TargetState, string? Resolution,
    string Reason, [property: JsonRequired] long ExpectedRevision, string? ExpectedClosureContext = null);
public sealed record RequestIncidentClosure(string Reason, [property: JsonRequired] long ExpectedRevision, string? ExpectedClosureContext = null);
public sealed record DecideIncidentClosure([property: JsonRequired] bool Approve, [property: JsonRequired] bool SelfApproval,
    string? SelfApprovalJustification, string Reason, [property: JsonRequired] long ExpectedRevision, string? ExpectedClosureContext = null);
public sealed record IncidentClosureReview(Guid Id, long RequestedAtRevision, Guid RequestedByUserId,
    ClosureReviewStatus Status, Guid? DecidedByUserId, long? DecisionRevision, bool? SelfApproval, string? SelfApprovalJustification);
