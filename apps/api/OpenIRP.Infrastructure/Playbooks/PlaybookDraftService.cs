using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;
using OpenIRP.Infrastructure.Audit;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Playbooks;

public sealed partial class PlaybookDraftService(OpenIrpDbContext db, AuditRecorder audit) : IPlaybookDraftService
{
    private static IdentityOperationException Error(int status, string code) => new(status, code);
    private async Task Authorize(Guid actor, Guid? organization, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct)) throw Error(403, "access_denied");
        if (organization is null)
        {
            if (!await db.PlatformAdministrators.AnyAsync(p => p.UserId == actor && p.RevokedAt == null, ct))
                throw Error(403, "access_denied");
        }
        else if (!await db.Organizations.AnyAsync(o => o.Id == organization && o.Status == OrganizationStatus.Active, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct))
            throw Error(403, "access_denied");
    }

    private async Task<T> Read<T>(Guid actor, Guid? organization, Guid? id, Func<Task<T>> operation, CancellationToken ct, bool publishedReader = false)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            if (publishedReader) await AuthorizePublished(actor, organization, ct); else await Authorize(actor, organization, ct);
            var result = await operation();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (IdentityOperationException e)
        {
            await audit.RejectedAsync(new("playbook.read", actor, organization, "playbook", id), e); throw;
        }
    }

    public Task<PlaybookPage> ListAsync(Guid actor, Guid? organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        Read(actor, organization, null, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_playbook_pagination");
            var entities = await db.Playbooks.AsNoTracking().Where(p => p.OrganizationId == organization)
                .OrderBy(p => p.Id).Skip(skip).Take(limit + 1).ToListAsync(ct);
            var more = entities.Count > limit;
            return new PlaybookPage(entities.Take(limit).Select(Summary).ToList(), more ? skip + limit : null);
        }, ct);

    public Task<PlaybookDraftItem> GetAsync(Guid actor, Guid? organization, Guid playbook, CancellationToken ct = default) =>
        Read(actor, organization, playbook, async () =>
        {
            var entity = await db.Playbooks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playbook && p.OrganizationId == organization, ct)
                ?? throw Error(404, "playbook_not_found");
            return await View(entity, ct);
        }, ct);

    public Task<PlaybookDraftItem> CreateAsync(Guid actor, Guid? organization, CreatePlaybookDraft request, CancellationToken ct = default) =>
        Write(actor, organization, null, "playbook.draft_created", async () =>
        {
            var content = request.Content;
            Validate(content);
            await References(content, null, ct);
            var now = DateTimeOffset.UtcNow;
            var entity = new Playbook { CreatedAt = now.AddTicks(-(now.Ticks % 10)), OrganizationId = organization, CategoryId = content.CategoryId,
                Title = content.Title.Trim(), CreatedByUserId = actor };
            var version = new PlaybookVersion { PlaybookId = entity.Id };
            db.Playbooks.Add(entity); db.PlaybookVersions.Add(version);
            Populate(version, content);
            return (entity, version);
        }, ct);

    public Task<PlaybookDraftItem> UpdateAsync(Guid actor, Guid? organization, Guid playbook, UpdatePlaybookDraft request, CancellationToken ct = default) =>
        Write(actor, organization, playbook, "playbook.draft_updated", async () =>
        {
            var entity = await db.Playbooks.SingleOrDefaultAsync(p => p.Id == playbook && p.OrganizationId == organization, ct)
                ?? throw Error(404, "playbook_not_found");
            if (entity.Revision != request.ExpectedRevision || entity.Revision == long.MaxValue) throw Error(409, "stale_playbook_revision");
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000) throw Error(400, "invalid_playbook_reason");
            Validate(request.Content);
            await References(request.Content, entity.CategoryId, ct);
            var version = await db.PlaybookVersions.SingleOrDefaultAsync(v => v.PlaybookId == entity.Id && v.Status == "DRAFT", ct)
                ?? throw Error(409, "playbook_version_not_editable");
            if (version.Status != "DRAFT") throw Error(409, "playbook_version_not_editable");
            // Replacement is limited to this draft. Deletes, new graph, revision and audit commit together.
            await db.PlaybookCapabilityRequirements.Where(r => r.VersionId == version.Id).ExecuteDeleteAsync(ct);
            await db.PlaybookStepDependencies.Where(d => d.VersionId == version.Id).ExecuteDeleteAsync(ct);
            await db.PlaybookSteps.Where(s => s.VersionId == version.Id).ExecuteDeleteAsync(ct);
            entity.Revision++;
            entity.Title = request.Content.Title.Trim(); entity.CategoryId = request.Content.CategoryId;
            Populate(version, request.Content);
            return (entity, version);
        }, ct);

    private async Task<PlaybookDraftItem> Write(Guid actor, Guid? organization, Guid? id, string action,
        Func<Task<(Playbook Entity, PlaybookVersion Version)>> operation, CancellationToken ct)
    {
        var attempt = new AuditAttempt(action, actor, organization, "playbook", id);
        try
        {
            if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean playbook scope.");
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)", ct);
            await Authorize(actor, organization, ct);
            var (entity, version) = await operation();
            db.AuditEvents.Add(audit.Success(attempt with { ResourceId = entity.Id },
                JsonSerializer.Serialize(new { entity.Revision, versionId = version.Id, version.VersionNumber, sourceVersionId = entity.SourceVersionId, sourceRevision = entity.SourceRevision })));
            await db.SaveChangesAsync(ct);
            var result = await View(entity, ct, version.Id);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception e)
        {
            Exception effective = e switch
            {
                DbUpdateConcurrencyException => Error(409, "concurrent_playbook_change"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => Error(409, "duplicate_playbook_content"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23503" or "23514" } } => Error(400, "invalid_playbook_reference_or_value"),
                PostgresException { SqlState: "55P03" or "40P01" or "40001" } => Error(409, "concurrent_change_retry"),
                DbUpdateException { InnerException: PostgresException { SqlState: "55P03" or "40P01" or "40001" } } => Error(409, "concurrent_change_retry"),
                _ => e
            };
            await audit.RejectedAsync(attempt, effective);
            if (ReferenceEquals(e, effective)) throw;
            throw effective;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private static void Validate(PlaybookDraftContent content)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.Title) || content.Title.Length > 200
            || string.IsNullOrWhiteSpace(content.Objective) || content.Objective.Length > 4000
            || string.IsNullOrWhiteSpace(content.Instructions) || content.Instructions.Length > 12000
            || content.Steps is null || content.Dependencies is null || content.Requirements is null
            || content.Steps.Count > 100 || content.Dependencies.Count > 500 || content.Requirements.Count > 300)
            throw Error(400, "invalid_playbook_content");
        foreach (var step in content.Steps)
            if (step is null || step.Code is null || !Regex.IsMatch(step.Code, @"\A[A-Z][A-Z0-9_]{0,59}\z")
                || step.Position is < 1 or > 10000 || string.IsNullOrWhiteSpace(step.Phase) || step.Phase.Length > 60
                || string.IsNullOrWhiteSpace(step.Instructions) || step.Instructions.Length > 8000)
                throw Error(400, "invalid_playbook_step");
        var codes = content.Steps.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        if (codes.Count != content.Steps.Count || content.Steps.Select(s => s.Position).Distinct().Count() != content.Steps.Count)
            throw Error(400, "duplicate_playbook_step");
        foreach (var dep in content.Dependencies)
            if (dep is null || dep.PredecessorCode is null || dep.SuccessorCode is null
                || !codes.Contains(dep.PredecessorCode) || !codes.Contains(dep.SuccessorCode))
                throw Error(400, "invalid_playbook_dependency");
        var edges = content.Dependencies.Select(d => (d.PredecessorCode, d.SuccessorCode)).ToList();
        if (edges.Distinct().Count() != edges.Count || !PlaybookDependencyGraph.IsAcyclic(codes, edges))
            throw Error(400, "invalid_playbook_dependency_graph");
        foreach (var req in content.Requirements)
            if (req is null || !Enum.IsDefined(req.Level) || (req.StepCode is not null && !codes.Contains(req.StepCode)))
                throw Error(400, "invalid_playbook_requirement");
        if (content.Requirements.Select(r => (r.StepCode, r.CapabilityId)).Distinct().Count() != content.Requirements.Count)
            throw Error(400, "duplicate_playbook_requirement");
    }

    private async Task References(PlaybookDraftContent content, Guid? previousCategory, CancellationToken ct)
    {
        var category = await db.IncidentCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == content.CategoryId, ct)
            ?? throw Error(400, "invalid_playbook_category");
        if (!category.IsActive && previousCategory != category.Id) throw Error(409, "inactive_playbook_category");
        var required = content.Requirements.Select(r => r.CapabilityId).Distinct().ToArray();
        if (await db.CapabilityCatalog.CountAsync(c => required.Contains(c.Id) && c.IsActive, ct) != required.Length)
            throw Error(409, "inactive_or_unknown_playbook_capability");
    }

    private void Populate(PlaybookVersion version, PlaybookDraftContent content)
    {
        version.Title = content.Title.Trim(); version.CategoryId = content.CategoryId;
        version.Objective = content.Objective.Trim(); version.Instructions = content.Instructions.Trim();
        var steps = content.Steps.Select(s => new PlaybookStep { VersionId = version.Id, Code = s.Code,
            Position = s.Position, Phase = s.Phase.Trim(), Instructions = s.Instructions.Trim(),
            IsRequired = s.IsRequired, RequiresApproval = s.RequiresApproval, RequiresEvidence = s.RequiresEvidence }).ToList();
        db.PlaybookSteps.AddRange(steps);
        var ids = steps.ToDictionary(s => s.Code, s => s.Id, StringComparer.Ordinal);
        db.PlaybookStepDependencies.AddRange(content.Dependencies.Select(d => new PlaybookStepDependency
            { VersionId = version.Id, PredecessorId = ids[d.PredecessorCode], SuccessorId = ids[d.SuccessorCode] }));
        db.PlaybookCapabilityRequirements.AddRange(content.Requirements.Select(r => new PlaybookCapabilityRequirement
            { VersionId = version.Id, CapabilityId = r.CapabilityId, StepId = r.StepCode is null ? null : ids[r.StepCode], Level = r.Level }));
    }

    private static PlaybookSummary Summary(Playbook e) => new(e.Id, e.OrganizationId,
        e.OrganizationId is null ? "PUBLIC" : "PRIVATE", e.CategoryId, e.Title, e.Revision, e.CreatedAt, e.CurrentPublishedVersionId, e.SourceVersionId, e.SourceRevision);
    private async Task<PlaybookDraftItem> View(Playbook entity, CancellationToken ct, Guid? versionId = null)
    {
        var query = db.PlaybookVersions.AsNoTracking().Where(v => v.PlaybookId == entity.Id);
        var version = versionId is not null ? await query.SingleAsync(v => v.Id == versionId, ct)
            : await query.OrderByDescending(v => v.VersionNumber).FirstAsync(ct);
        var steps = await db.PlaybookSteps.AsNoTracking().Where(s => s.VersionId == version.Id).OrderBy(s => s.Position)
            .Select(s => new PlaybookStepItem(s.Id, s.Code, s.Position, s.Phase, s.Instructions, s.IsRequired, s.RequiresApproval, s.RequiresEvidence)).ToListAsync(ct);
        var dependencies = await db.PlaybookStepDependencies.AsNoTracking().Where(d => d.VersionId == version.Id).OrderBy(d => d.Id)
            .Select(d => new PlaybookDependencyItem(d.Id, d.PredecessorId, d.SuccessorId)).ToListAsync(ct);
        var requirements = await db.PlaybookCapabilityRequirements.AsNoTracking().Where(r => r.VersionId == version.Id).OrderBy(r => r.Id)
            .Select(r => new PlaybookRequirementItem(r.Id, r.CapabilityId, r.StepId, r.Level)).ToListAsync(ct);
        return new(Summary(entity) with { Title = version.Title, CategoryId = version.CategoryId }, version.Id, version.VersionNumber, version.Status,
            version.Objective, version.Instructions, steps, dependencies, requirements, version.ReviewRequestId, version.SourceVersionId, version.PublishedAt);
    }
}
