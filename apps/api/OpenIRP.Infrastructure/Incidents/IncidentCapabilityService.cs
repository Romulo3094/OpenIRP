using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<IncidentCapabilityPage> CapabilitiesAsync(Guid actor, Guid organization, Guid incident,
        int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(actor, organization, incident, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_incident_pagination");
            // The aggregate revision and all dependency versions belong to one PostgreSQL snapshot.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await AuthorizeAsync(actor, organization, false, ct);
            var parent = await db.Incidents.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId == organization && e.Id == incident, ct)
                ?? throw Error(404, "incident_not_found");
            var query = from catalog in db.CapabilityCatalog.AsNoTracking()
                        join profile in db.OrganizationCapabilities.AsNoTracking().Where(e => e.OrganizationId == organization)
                            on catalog.Id equals profile.CapabilityId into profiles
                        from profile in profiles.DefaultIfEmpty()
                        join assessment in db.IncidentCapabilityAssessments.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident)
                            on catalog.Id equals assessment.CapabilityId into assessments
                        from assessment in assessments.DefaultIfEmpty()
                        where catalog.IsActive || profile != null || assessment != null
                        orderby catalog.Code
                        select new { Catalog = catalog, Profile = profile, Assessment = assessment };
            var rows = await query.Skip(skip).Take(limit + 1).ToListAsync(ct);
            var items = rows.Take(limit).Select(e => CapabilityView(e.Catalog, e.Profile, e.Assessment)).ToList();
            await tx.CommitAsync(ct);
            return new IncidentCapabilityPage(items, rows.Count > limit ? skip + limit : null, parent.Revision);
        }, ct);

    public async Task<IncidentCapabilityChange> AssessCapabilityAsync(Guid actor, Guid organization, Guid incident,
        Guid capability, AssessIncidentCapability request, CancellationToken ct = default)
    {
        IncidentCapabilityItem item = null!;
        var action = request.Availability is null ? "CAPABILITY_INHERITED" : "CAPABILITY_ASSESSED";
        var result = await WriteAsync(actor, organization, incident, action, async () =>
        {
            var parent = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(parent, request.ExpectedRevision);
            if ((request.Availability is { } availability && !Enum.IsDefined(availability)) || request.Limitations?.Length > 2000)
                throw Error(400, "invalid_incident_capability");
            if (request.ExpectedOrganizationCapabilityRevision < 0 || request.ExpectedCatalogRevision < 1)
                throw Error(400, "invalid_capability_precondition");
            var catalog = await db.CapabilityCatalog.AsNoTracking().SingleOrDefaultAsync(e => e.Id == capability, ct)
                ?? throw Error(404, "capability_not_found");
            var profile = await db.OrganizationCapabilities.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId == organization && e.CapabilityId == capability, ct);
            if (catalog.Revision != request.ExpectedCatalogRevision || (profile?.Revision ?? 0) != request.ExpectedOrganizationCapabilityRevision)
                throw Error(409, "stale_capability_context");
            if (!catalog.IsActive && request.Availability is not null) throw Error(409, "inactive_capability");
            var assessment = await db.IncidentCapabilityAssessments.SingleOrDefaultAsync(e => e.OrganizationId == organization && e.IncidentId == incident && e.CapabilityId == capability, ct);
            if (assessment is null)
            {
                assessment = new() { OrganizationId = organization, IncidentId = incident, CapabilityId = capability };
                db.IncidentCapabilityAssessments.Add(assessment);
            }
            assessment.Availability = request.Availability;
            assessment.Limitations = string.IsNullOrWhiteSpace(request.Limitations) ? null : request.Limitations.Trim();
            assessment.Reason = request.Reason.Trim(); assessment.AssessedAtRevision = parent.Revision;
            item = CapabilityView(catalog, profile, assessment);
            return (parent, assessment.Reason);
        }, ct, () => CapabilityDetails(item));
        return new(item, result.Revision);
    }

    private static IncidentCapabilityItem CapabilityView(CapabilityCatalogEntry catalog, OrganizationCapability? profile, IncidentCapabilityAssessment? assessment)
    {
        var effective = !catalog.IsActive ? CapabilityAvailability.Unknown
            : assessment?.Availability ?? profile?.Availability ?? CapabilityAvailability.Unknown;
        var source = !catalog.IsActive ? IncidentCapabilitySource.InactiveCatalog
            : assessment?.Availability is not null ? IncidentCapabilitySource.IncidentAssessment
            : profile is not null ? IncidentCapabilitySource.OrganizationProfile : IncidentCapabilitySource.Undeclared;
        return new(catalog.Id, catalog.Code, catalog.Name, catalog.IsActive, catalog.Revision,
            profile?.Availability ?? CapabilityAvailability.Unknown, profile?.Notes, profile?.Revision ?? 0,
            assessment?.Availability, effective, source, assessment?.Limitations, assessment?.Id, assessment?.AssessedAtRevision, assessment?.Reason);
    }

    private static string CapabilityDetails(IncidentCapabilityItem item) => JsonSerializer.Serialize(new
    {
        capability = new
        {
            item.CapabilityId, item.Code, item.Name, item.IsActive, item.CatalogRevision,
            item.OrganizationAvailability, item.OrganizationNotes, item.OrganizationCapabilityRevision,
            item.OverrideAvailability, item.EffectiveAvailability, item.Source, item.Limitations,
            item.AssessmentId, item.AssessedAtRevision
        }
    }, DetailsOptions); // Reason belongs to the parent event, not another copy inside its JSON payload.
}
