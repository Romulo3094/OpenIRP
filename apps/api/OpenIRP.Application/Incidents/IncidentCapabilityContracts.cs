using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public enum IncidentCapabilitySource { Undeclared, OrganizationProfile, IncidentAssessment, InactiveCatalog }
public sealed record AssessIncidentCapability([property: JsonRequired] CapabilityAvailability? Availability,
    string? Limitations, string Reason, [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] long ExpectedOrganizationCapabilityRevision, [property: JsonRequired] long ExpectedCatalogRevision);
public sealed record IncidentCapabilityItem(Guid CapabilityId, string Code, string Name, bool IsActive, long CatalogRevision,
    CapabilityAvailability OrganizationAvailability, string? OrganizationNotes, long OrganizationCapabilityRevision,
    CapabilityAvailability? OverrideAvailability, CapabilityAvailability EffectiveAvailability, IncidentCapabilitySource Source,
    string? Limitations, Guid? AssessmentId, long? AssessedAtRevision, string? Reason);
public sealed record IncidentCapabilityPage(IReadOnlyList<IncidentCapabilityItem> Items, int? NextSkip, long IncidentRevision);
public sealed record IncidentCapabilityChange(IncidentCapabilityItem Capability, long IncidentRevision);
