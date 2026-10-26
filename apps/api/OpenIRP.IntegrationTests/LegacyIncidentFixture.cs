using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    // A fixture representing data written before severity activation; never a production write path.
    private async Task<IncidentItem> RegisterLegacyIncident(Guid actor, Guid organization)
    {
        var category = await IncidentCategoryId();
        var id = await InScope(async (_, db) =>
        {
            var incident = new Incident { OrganizationId = organization, CategoryId = category, Title = "Incidente anterior a severidad" };
            db.Incidents.Add(incident);
            db.IncidentEvents.Add(new() { OrganizationId = organization, IncidentId = incident.Id, Revision = 1,
                ActorUserId = actor, CorrelationId = Guid.NewGuid(), Action = "REGISTERED", CategoryId = category,
                Title = incident.Title, OccurredAt = incident.CreatedAt });
            await db.SaveChangesAsync();
            return incident.Id;
        });
        return await Incidents(s => s.GetAsync(actor, organization, id));
    }
}
