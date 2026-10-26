using System.Text.Json.Serialization;

namespace OpenIRP.Application.Playbooks;

public sealed record PlaybookVersionCommand(string Reason, [property: JsonRequired] long ExpectedRevision);
public sealed record NewPlaybookVersion([property: JsonRequired] Guid SourceVersionId, string Reason,
    [property: JsonRequired] long ExpectedRevision);
public sealed record DecidePlaybookPublication([property: JsonRequired] bool Publish,
    [property: JsonRequired] bool SelfApproval, string? SelfApprovalJustification, string Reason,
    [property: JsonRequired] long ExpectedRevision);
public sealed record PlaybookVersionSummary(Guid Id, int VersionNumber, string Status, string Title,
    Guid CategoryId, Guid? SourceVersionId, Guid? ReviewRequestId, Guid? SubmittedByUserId, DateTimeOffset? PublishedAt);
public sealed record PlaybookReviewItem(Guid Id, Guid VersionId, Guid? RequestId, string Action, Guid ActorUserId,
    long Revision, string Reason, bool SelfApproval, string? SelfApprovalJustification,
    string ContentHash, DateTimeOffset OccurredAt);
