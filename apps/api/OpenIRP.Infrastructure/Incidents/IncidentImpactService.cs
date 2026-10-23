using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentItem> AssessImpactAsync(Guid actor, Guid organization, Guid incident,
        AssessIncidentImpact request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, incident, "IMPACT_ASSESSED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if (!ValidImpact(request.ConfidentialityImpact) || !ValidImpact(request.IntegrityImpact) || !ValidImpact(request.AvailabilityImpact))
                throw Error(400, "invalid_incident_impact");
            entity.ConfidentialityImpact = request.ConfidentialityImpact;
            entity.IntegrityImpact = request.IntegrityImpact;
            entity.AvailabilityImpact = request.AvailabilityImpact;
            return (entity, request.Reason.Trim());
        }, ct);

    private static bool ValidImpact(ImpactLevel? impact) => impact is null || Enum.IsDefined(impact.Value);
}
