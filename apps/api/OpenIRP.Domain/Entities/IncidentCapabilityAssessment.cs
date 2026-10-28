namespace OpenIRP.Domain.Entities;

public sealed class IncidentCapabilityAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid CapabilityId { get; set; }
    // Null inherits; explicit Unknown overrides any declaration in the organization profile.
    public CapabilityAvailability? Availability { get; set; }
    public string? Limitations { get; set; }
    public string Reason { get; set; } = "";
    public long AssessedAtRevision { get; set; }
}
