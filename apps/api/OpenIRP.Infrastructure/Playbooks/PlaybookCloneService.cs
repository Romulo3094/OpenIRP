using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Playbooks;

public sealed partial class PlaybookDraftService
{
    public Task<PlaybookDraftItem> CloneAsync(Guid actor, Guid? organization, ClonePlaybook request, CancellationToken ct = default) =>
        Write(actor, organization, null, "playbook.cloned", async () =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
                throw Error(400, "invalid_playbook_reason");
            // Destination administration has already been revalidated under the shared gate.
            // Never turn a source UUID into cross-organization or private-to-public access.
            var source = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.SourcePlaybookId
                && (p.OrganizationId == null || (organization != null && p.OrganizationId == organization)), ct)
                ?? throw Error(404, "playbook_source_not_found");
            if (source.Revision != request.ExpectedSourceRevision) throw Error(409, "stale_playbook_source_revision");
            if (source.CurrentPublishedVersionId != request.SourceVersionId)
                throw Error(409, "playbook_source_not_current");
            var sourceVersion = await db.PlaybookVersions.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == request.SourceVersionId && v.PlaybookId == source.Id, ct)
                ?? throw Error(404, "playbook_source_not_found");
            if (sourceVersion.Status != "PUBLISHED" || sourceVersion.PublishedAt is null)
                throw Error(409, "playbook_source_not_published");
            var content = Content(await View(source, ct, sourceVersion.Id));
            Validate(content);
            await References(content, null, ct);
            var now = DateTimeOffset.UtcNow;
            var entity = new Playbook { OrganizationId = organization, CreatedByUserId = actor,
                CreatedAt = now.AddTicks(-(now.Ticks % 10)), Title = content.Title, CategoryId = content.CategoryId,
                SourceVersionId = sourceVersion.Id, SourceRevision = source.Revision, CloneReason = request.Reason.Trim() };
            var version = new PlaybookVersion { PlaybookId = entity.Id };
            db.Playbooks.Add(entity); db.PlaybookVersions.Add(version);
            Populate(version, content);
            return (entity, version);
        }, ct);

    public Task<PlaybookOriginItem> OriginAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default) =>
        Read(actor, organization, playbook, async () =>
        {
            var entity = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organization && p.Id == playbook, ct)
                ?? throw Error(404, "playbook_not_found");
            Guid? sourcePlaybook = entity.SourceVersionId is Guid id
                ? await db.PlaybookVersions.AsNoTracking().Where(v => v.Id == id).Select(v => (Guid?)v.PlaybookId).SingleAsync(ct)
                : null;
            return new PlaybookOriginItem(entity.Id, sourcePlaybook, entity.SourceVersionId, entity.SourceRevision,
                entity.CloneReason, entity.CreatedByUserId, entity.CreatedAt);
        }, ct);
}
