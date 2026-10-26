using System.Text.Json.Serialization;

namespace OpenIRP.Application.Incidents;

// The server calculates severity; clients provide only a reason and the observed revision.
public sealed record AssessIncidentSeverity(string Reason, [property: JsonRequired] long ExpectedRevision);
