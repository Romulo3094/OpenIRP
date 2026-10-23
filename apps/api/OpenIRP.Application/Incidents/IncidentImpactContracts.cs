using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record AssessIncidentImpact(
    [property: JsonRequired] ImpactLevel? ConfidentialityImpact,
    [property: JsonRequired] ImpactLevel? IntegrityImpact,
    [property: JsonRequired] ImpactLevel? AvailabilityImpact,
    string Reason, [property: JsonRequired] long ExpectedRevision);
