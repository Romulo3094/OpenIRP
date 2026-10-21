namespace OpenIRP.Domain.Entities;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Sector { get; set; }
    public required string CountryCode { get; set; }
    public string? SizeBand { get; set; }
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
