using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Playbooks;

public sealed record PlaybookStepInput(string Code, int Position, string Phase, string Instructions,
    [property: JsonRequired] bool IsRequired, [property: JsonRequired] bool RequiresApproval,
    [property: JsonRequired] bool RequiresEvidence);
public sealed record PlaybookDependencyInput(string PredecessorCode, string SuccessorCode);
public sealed record PlaybookRequirementInput(Guid CapabilityId, string? StepCode,
    [property: JsonRequired] PlaybookRequirementLevel Level);
public sealed record PlaybookDraftContent(string Title, Guid CategoryId, string Objective, string Instructions,
    IReadOnlyList<PlaybookStepInput> Steps, IReadOnlyList<PlaybookDependencyInput> Dependencies,
    IReadOnlyList<PlaybookRequirementInput> Requirements);
public sealed record CreatePlaybookDraft([property: JsonRequired] PlaybookDraftContent Content);
public sealed record UpdatePlaybookDraft([property: JsonRequired] PlaybookDraftContent Content, string Reason,
    [property: JsonRequired] long ExpectedRevision);
public sealed record PlaybookSummary(Guid Id, Guid? OrganizationId, string Visibility, Guid CategoryId,
    string Title, long Revision, DateTimeOffset CreatedAt, Guid? CurrentPublishedVersionId = null, Guid? SourceVersionId = null, long? SourceRevision = null);
public sealed record PlaybookStepItem(Guid Id, string Code, int Position, string Phase, string Instructions,
    bool IsRequired, bool RequiresApproval, bool RequiresEvidence);
public sealed record PlaybookDependencyItem(Guid Id, Guid PredecessorId, Guid SuccessorId);
public sealed record PlaybookRequirementItem(Guid Id, Guid CapabilityId, Guid? StepId, PlaybookRequirementLevel Level);
public sealed record PlaybookDraftItem(PlaybookSummary Playbook, Guid VersionId, int VersionNumber, string Status,
    string Objective, string Instructions, IReadOnlyList<PlaybookStepItem> Steps,
    IReadOnlyList<PlaybookDependencyItem> Dependencies, IReadOnlyList<PlaybookRequirementItem> Requirements,
    Guid? ReviewRequestId = null, Guid? SourceVersionId = null, DateTimeOffset? PublishedAt = null);
public sealed record PlaybookPage(IReadOnlyList<PlaybookSummary> Items, int? NextSkip);

public interface IPlaybookDraftService
{
    Task<IReadOnlyList<PlaybookVersionSummary>> VersionsAsync(Guid actor, Guid? organization, Guid playbook, int afterNumber = 0, int limit = 50, CancellationToken ct = default);
    Task<PlaybookDraftItem> VersionAsync(Guid actor, Guid? organization, Guid playbook, Guid version, CancellationToken ct = default);
    Task<IReadOnlyList<PlaybookReviewItem>> ReviewsAsync(Guid actor, Guid? organization, Guid playbook, long afterRevision = 0, int limit = 50, CancellationToken ct = default);
    Task<PlaybookDraftItem> NewVersionAsync(Guid actor, Guid? organization, Guid playbook, NewPlaybookVersion request, CancellationToken ct = default);
    Task<PlaybookDraftItem> SubmitAsync(Guid actor, Guid? organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct = default);
    Task<PlaybookDraftItem> DecideAsync(Guid actor, Guid? organization, Guid playbook, Guid version, Guid reviewRequest, DecidePlaybookPublication request, CancellationToken ct = default);
    Task<PlaybookDraftItem> WithdrawAsync(Guid actor, Guid? organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct = default);
    Task<PlaybookPage> ListCurrentAsync(Guid actor, Guid? organization, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<PlaybookDraftItem> CurrentAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default);
    Task<PlaybookDraftItem> CloneAsync(Guid actor, Guid? organization, ClonePlaybook request, CancellationToken ct = default);
    Task<PlaybookOriginItem> OriginAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default);
    // Null scope means global administration; never tenant membership bypass.
    Task<PlaybookPage> ListAsync(Guid actor, Guid? organization, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<PlaybookDraftItem> GetAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default);
    Task<PlaybookDraftItem> CreateAsync(Guid actor, Guid? organization, CreatePlaybookDraft request, CancellationToken ct = default);
    Task<PlaybookDraftItem> UpdateAsync(Guid actor, Guid? organization, Guid playbook, UpdatePlaybookDraft request, CancellationToken ct = default);
}
