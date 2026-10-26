using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Playbooks;

public sealed partial class PlaybookDraftService
{
    private async Task<Playbook> Find(Guid? organization, Guid id, long expected, string reason, CancellationToken ct)
    {
        var entity = await db.Playbooks.SingleOrDefaultAsync(p => p.Id == id && p.OrganizationId == organization, ct)
            ?? throw Error(404, "playbook_not_found");
        if (expected != entity.Revision || entity.Revision == long.MaxValue) throw Error(409, "stale_playbook_revision");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw Error(400, "invalid_playbook_reason");
        entity.Revision++;
        return entity;
    }

    private async Task<PlaybookVersion> FindVersion(Guid playbook, Guid version, CancellationToken ct) =>
        await db.PlaybookVersions.SingleOrDefaultAsync(v => v.PlaybookId == playbook && v.Id == version, ct)
            ?? throw Error(404, "playbook_version_not_found");

    public Task<IReadOnlyList<PlaybookVersionSummary>> VersionsAsync(Guid actor, Guid? organization, Guid playbook,
        int afterNumber = 0, int limit = 50, CancellationToken ct = default) =>
        Read<IReadOnlyList<PlaybookVersionSummary>>(actor, organization, playbook, async () =>
        {
            if (afterNumber < 0 || limit is < 1 or > 100) throw Error(400, "invalid_playbook_pagination");
            if (!await db.Playbooks.AnyAsync(p => p.OrganizationId == organization && p.Id == playbook, ct)) throw Error(404, "playbook_not_found");
            return await db.PlaybookVersions.AsNoTracking().Where(v => v.PlaybookId == playbook && v.VersionNumber > afterNumber)
                .OrderBy(v => v.VersionNumber).Take(limit)
                .Select(v => new PlaybookVersionSummary(v.Id, v.VersionNumber, v.Status, v.Title, v.CategoryId,
                    v.SourceVersionId, v.ReviewRequestId, v.SubmittedByUserId, v.PublishedAt)).ToListAsync(ct);
        }, ct);

    public Task<PlaybookDraftItem> VersionAsync(Guid actor, Guid? organization, Guid playbook, Guid version, CancellationToken ct = default) =>
        Read(actor, organization, playbook, async () =>
        {
            var entity = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organization && p.Id == playbook, ct)
                ?? throw Error(404, "playbook_not_found");
            await FindVersion(playbook, version, ct);
            return await View(entity, ct, version);
        }, ct);

    public Task<IReadOnlyList<PlaybookReviewItem>> ReviewsAsync(Guid actor, Guid? organization, Guid playbook,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        Read<IReadOnlyList<PlaybookReviewItem>>(actor, organization, playbook, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_playbook_pagination");
            if (!await db.Playbooks.AnyAsync(p => p.OrganizationId == organization && p.Id == playbook, ct)) throw Error(404, "playbook_not_found");
            return await db.PlaybookReviews.AsNoTracking().Where(r => r.PlaybookId == playbook && r.Revision > afterRevision)
                .OrderBy(r => r.Revision).Take(limit)
                .Select(r => new PlaybookReviewItem(r.Id, r.VersionId, r.RequestId, r.Action, r.ActorUserId, r.Revision,
                    r.Reason, r.SelfApproval, r.SelfApprovalJustification, r.ContentHash, r.OccurredAt)).ToListAsync(ct);
        }, ct);

    public Task<PlaybookDraftItem> NewVersionAsync(Guid actor, Guid? organization, Guid playbook, NewPlaybookVersion request, CancellationToken ct = default) =>
        Write(actor, organization, playbook, "playbook.version_created", async () =>
        {
            var entity = await Find(organization, playbook, request.ExpectedRevision, request.Reason, ct);
            if (await db.PlaybookVersions.AnyAsync(v => v.PlaybookId == playbook && (v.Status == "DRAFT" || v.Status == "IN_REVIEW"), ct))
                throw Error(409, "playbook_draft_already_exists");
            var source = await FindVersion(playbook, request.SourceVersionId, ct);
            if (source.PublishedAt is null) throw Error(409, "playbook_source_not_published");
            var lastNumber = await db.PlaybookVersions.Where(v => v.PlaybookId == playbook).MaxAsync(v => v.VersionNumber, ct);
            if (lastNumber == int.MaxValue) throw Error(409, "playbook_version_limit");
            var content = Content(await View(entity, ct, source.Id));
            var version = new PlaybookVersion { PlaybookId = playbook, VersionNumber = lastNumber + 1, SourceVersionId = source.Id };
            db.PlaybookVersions.Add(version);
            entity.Title = source.Title; entity.CategoryId = source.CategoryId;
            Populate(version, content);
            return (entity, version);
        }, ct);

    public Task<PlaybookDraftItem> SubmitAsync(Guid actor, Guid? organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct = default) =>
        Write(actor, organization, playbook, "playbook.review_requested", async () =>
        {
            var entity = await Find(organization, playbook, request.ExpectedRevision, request.Reason, ct);
            var draft = await FindVersion(playbook, version, ct);
            if (draft.Status != "DRAFT") throw Error(409, "playbook_version_not_editable");
            var content = Content(await View(entity, ct, version));
            await Publishable(content, ct);
            draft.Status = "IN_REVIEW"; draft.ReviewRequestId = Guid.NewGuid(); draft.SubmittedByUserId = actor;
            Review(entity, draft, actor, "SUBMITTED", request.Reason, content);
            return (entity, draft);
        }, ct);

    public Task<PlaybookDraftItem> DecideAsync(Guid actor, Guid? organization, Guid playbook, Guid version, Guid reviewRequest,
        DecidePlaybookPublication request, CancellationToken ct = default) =>
        Write(actor, organization, playbook, request.Publish ? "playbook.published" : "playbook.review_rejected", async () =>
        {
            var entity = await Find(organization, playbook, request.ExpectedRevision, request.Reason, ct);
            var draft = await FindVersion(playbook, version, ct);
            if (draft.ReviewRequestId != reviewRequest) throw Error(404, "playbook_review_not_found");
            if (draft.Status != "IN_REVIEW") throw Error(409, "playbook_review_not_pending");
            var self = request.Publish && draft.SubmittedByUserId == actor;
            if (request.SelfApproval != self || (self && (string.IsNullOrWhiteSpace(request.SelfApprovalJustification)
                || request.SelfApprovalJustification.Length > 2000))
                || (!self && request.SelfApprovalJustification is not null))
                throw Error(400, "invalid_playbook_self_approval");
            var content = Content(await View(entity, ct, version));
            if (request.Publish)
            {
                await Authorize(draft.SubmittedByUserId!.Value, organization, ct);
                await Publishable(content, ct);
                draft.Status = "PUBLISHED";
                var now = DateTimeOffset.UtcNow; draft.PublishedAt = now.AddTicks(-(now.Ticks % 10));
                // Prior publications remain readable; only this explicit pointer selects the current version.
                entity.CurrentPublishedVersionId = draft.Id;
            }
            Review(entity, draft, actor, request.Publish ? "PUBLISHED" : "REJECTED", request.Reason, content,
                self, request.SelfApprovalJustification);
            if (!request.Publish)
            {
                draft.Status = "DRAFT"; draft.ReviewRequestId = null; draft.SubmittedByUserId = null;
            }
            return (entity, draft);
        }, ct);

    public Task<PlaybookDraftItem> WithdrawAsync(Guid actor, Guid? organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct = default) =>
        Write(actor, organization, playbook, "playbook.withdrawn", async () =>
        {
            var entity = await Find(organization, playbook, request.ExpectedRevision, request.Reason, ct);
            var published = await FindVersion(playbook, version, ct);
            if (published.Status != "PUBLISHED") throw Error(409, "playbook_version_not_published");
            published.Status = "ARCHIVED";
            if (entity.CurrentPublishedVersionId == version) entity.CurrentPublishedVersionId = null;
            Review(entity, published, actor, "WITHDRAWN", request.Reason, Content(await View(entity, ct, version)));
            return (entity, published);
        }, ct);

    public Task<PlaybookDraftItem> CurrentAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default) =>
        Read(actor, organization, playbook, async () =>
        {
            var entity = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organization && p.Id == playbook, ct)
                ?? throw Error(404, "playbook_not_found");
            if (entity.CurrentPublishedVersionId is not Guid id) throw Error(404, "playbook_has_no_current_version");
            var version = await FindVersion(playbook, id, ct);
            if (version.Status != "PUBLISHED") throw Error(409, "invalid_playbook_current_version");
            return await View(entity, ct, id);
        }, ct, publishedReader: true);

    public Task<PlaybookPage> ListCurrentAsync(Guid actor, Guid? organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Read(actor, organization, null, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_playbook_pagination");
            var query = from p in db.Playbooks.AsNoTracking()
                        join v in db.PlaybookVersions.AsNoTracking() on p.CurrentPublishedVersionId equals v.Id
                        where p.OrganizationId == organization && v.Status == "PUBLISHED"
                        orderby p.Id
                        select new PlaybookSummary(p.Id, p.OrganizationId, p.OrganizationId == null ? "PUBLIC" : "PRIVATE",
                            v.CategoryId, v.Title, p.Revision, p.CreatedAt, p.CurrentPublishedVersionId, p.SourceVersionId, p.SourceRevision);
            var rows = await query.Skip(skip).Take(limit + 1).ToListAsync(ct);
            return new PlaybookPage(rows.Take(limit).ToList(), rows.Count > limit ? skip + limit : null);
        }, ct, publishedReader: true);

    private async Task AuthorizePublished(Guid actor, Guid? organization, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct)) throw Error(403, "access_denied");
        if (organization is not null &&
            (!await db.Organizations.AnyAsync(o => o.Id == organization && o.Status == OrganizationStatus.Active, ct)
             || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor && m.Status == MembershipStatus.Active, ct)))
            throw Error(403, "access_denied");
    }

    private async Task Publishable(PlaybookDraftContent content, CancellationToken ct)
    {
        Validate(content);
        if (content.Steps.Count == 0) throw Error(409, "playbook_has_no_steps");
        await References(content, null, ct);
    }

    private static PlaybookDraftContent Content(PlaybookDraftItem item)
    {
        var codes = item.Steps.ToDictionary(s => s.Id, s => s.Code);
        return new(item.Playbook.Title, item.Playbook.CategoryId, item.Objective, item.Instructions,
            item.Steps.Select(s => new PlaybookStepInput(s.Code, s.Position, s.Phase, s.Instructions,
                s.IsRequired, s.RequiresApproval, s.RequiresEvidence)).ToList(),
            item.Dependencies.Select(d => new PlaybookDependencyInput(codes[d.PredecessorId], codes[d.SuccessorId])).ToList(),
            item.Requirements.Select(r => new PlaybookRequirementInput(r.CapabilityId, r.StepId is null ? null : codes[r.StepId.Value], r.Level)).ToList());
    }

    private void Review(Playbook entity, PlaybookVersion version, Guid actor, string action, string reason,
        PlaybookDraftContent content, bool self = false, string? justification = null)
    {
        // Canonical ordering is independent of UUID allocation and database query order.
        var canonical = content with
        {
            Steps = content.Steps.OrderBy(s => s.Code, StringComparer.Ordinal).ToList(),
            Dependencies = content.Dependencies.OrderBy(d => d.PredecessorCode, StringComparer.Ordinal).ThenBy(d => d.SuccessorCode, StringComparer.Ordinal).ToList(),
            Requirements = content.Requirements.OrderBy(r => r.StepCode, StringComparer.Ordinal).ThenBy(r => r.CapabilityId).ToList()
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
        db.PlaybookReviews.Add(new() { PlaybookId = entity.Id, VersionId = version.Id,
            RequestId = action == "WITHDRAWN" ? null : version.ReviewRequestId, Action = action, ActorUserId = actor,
            Revision = entity.Revision, Reason = reason.Trim(), ContentHash = hash,
            SelfApproval = self, SelfApprovalJustification = self ? justification!.Trim() : null });
    }
}
