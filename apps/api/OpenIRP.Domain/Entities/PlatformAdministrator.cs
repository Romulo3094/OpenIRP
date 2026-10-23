namespace OpenIRP.Domain.Entities;

public sealed class PlatformAdministrator
{
    public Guid UserId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? GrantedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}
