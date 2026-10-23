namespace OpenIRP.Domain.Entities;

// Workflow remains REGISTERED while CIA assessment is recorded independently.
public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "REGISTERED";
    public ImpactLevel? ConfidentialityImpact { get; set; }
    public ImpactLevel? IntegrityImpact { get; set; }
    public ImpactLevel? AvailabilityImpact { get; set; }
    public Guid? AssignedMembershipId { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Full business snapshot at each revision; protected from UPDATE/DELETE/TRUNCATE.
public sealed class IncidentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public long Revision { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Action { get; set; } = "";
    public string DetailsJson { get; set; } = "{}";
    public string? Reason { get; set; }
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "REGISTERED";
    public ImpactLevel? ConfidentialityImpact { get; set; }
    public ImpactLevel? IntegrityImpact { get; set; }
    public ImpactLevel? AvailabilityImpact { get; set; }
    public Guid? AssignedMembershipId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
