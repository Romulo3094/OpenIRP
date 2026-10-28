using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record AssessIncidentCriticality(
    [property: JsonRequired] IncidentCriticality? AssessedCriticality,
    string? ReevaluationJustification, string Reason,
    [property: JsonRequired] long ExpectedRevision);
