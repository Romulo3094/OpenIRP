namespace OpenIRP.Domain.Entities;

public sealed class PlaybookReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaybookId { get; set; }
    public Guid VersionId { get; set; }
    public Guid? RequestId { get; set; }
    public string Action { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public long Revision { get; set; }
    public string Reason { get; set; } = "";
    public bool SelfApproval { get; set; }
    public string? SelfApprovalJustification { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
