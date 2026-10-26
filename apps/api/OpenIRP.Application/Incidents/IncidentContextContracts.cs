using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

// Both inputs must be supplied explicitly; null records that evidence is still missing.
public sealed record AssessIncidentContext(
    [property: JsonRequired] IncidentScope? Scope,
    [property: JsonRequired] OperationalImpact? OperationalImpact,
    string Reason, [property: JsonRequired] long ExpectedRevision);
