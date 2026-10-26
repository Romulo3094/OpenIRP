using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentItem> AssessContextAsync(Guid actor, Guid organization, Guid incident,
        AssessIncidentContext request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, incident, "CONTEXT_ASSESSED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if ((request.Scope.HasValue && !Enum.IsDefined(request.Scope.Value))
                || (request.OperationalImpact.HasValue && !Enum.IsDefined(request.OperationalImpact.Value)))
                throw Error(400, "invalid_incident_context");
            entity.Scope = request.Scope;
            entity.OperationalImpact = request.OperationalImpact;
            return (entity, request.Reason.Trim());
        }, ct);
}
