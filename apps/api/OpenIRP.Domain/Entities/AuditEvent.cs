namespace OpenIRP.Domain.Entities;

// Historical identifiers are retained without cascade relationships to mutable identity data.
public sealed class AuditEvent
{
    public long Id { get; private set; }
    public Guid? OrganizationId { get; init; }
    public Guid? ActorUserId { get; init; }
    public required string ActorType { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public required string Outcome { get; init; }
    public Guid CorrelationId { get; init; }
    public string DetailsJson { get; init; } = "{}";
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
