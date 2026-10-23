namespace OpenIRP.Domain.Entities;

public sealed class OrganizationMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
