using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIRP.Application.Audit;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Audit;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService(OpenIrpDbContext db, AuditRecorder audit, AuditContext context) : IIncidentService
{
    private static IdentityOperationException Error(int status, string code) => new(status, code);

    private async Task<IncidentItem> WriteAsync(Guid actor, Guid organization, Guid? id, string action,
        Func<Task<(Incident Entity, string? Reason)>> operation, CancellationToken ct, Func<string>? details = null)
    {
        var attempt = new AuditAttempt("incident." + action.ToLowerInvariant(), actor, organization, "incident", id);
        try
        {
            if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean incident scope.");
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)", ct);
            await AuthorizeAsync(actor, organization, true, ct);
            var (entity, reason) = await operation();
            db.IncidentEvents.Add(new()
            {
                OrganizationId = organization, IncidentId = entity.Id, Revision = entity.Revision,
                ActorUserId = actor, CorrelationId = context.CorrelationId, Action = action, Reason = reason,
                CategoryId = entity.CategoryId, Title = entity.Title, Description = entity.Description,
                Status = entity.Status, AssignedMembershipId = entity.AssignedMembershipId, DetailsJson = details?.Invoke() ?? "{}",
                ConfidentialityImpact = entity.ConfidentialityImpact, IntegrityImpact = entity.IntegrityImpact,
                AvailabilityImpact = entity.AvailabilityImpact
            });
            db.AuditEvents.Add(audit.Success(attempt with { ResourceId = entity.Id },
                JsonSerializer.Serialize(new { entity.Revision, entity.CategoryId, entity.AssignedMembershipId })));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(entity);
        }
        catch (Exception e)
        {
            Exception effective = e switch
            {
                DbUpdateConcurrencyException => Error(409, "concurrent_incident_change"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => Error(409, "concurrent_incident_change"),
                DbUpdateException { InnerException: PostgresException { SqlState: "23503" or "23514" } } => Error(400, "invalid_incident_reference_or_value"),
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

    private async Task<T> ReadAsync<T>(Guid actor, Guid organization, Guid? id, Func<Task<T>> read, CancellationToken ct)
    {
        try { await AuthorizeAsync(actor, organization, false, ct); return await read(); }
        catch (IdentityOperationException e)
        {
            await audit.RejectedAsync(new("incident.read", actor, organization, "incident", id), e);
            throw;
        }
    }

    private async Task AuthorizeAsync(Guid actor, Guid organization, bool write, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct)
            || !await db.Organizations.AnyAsync(o => o.Id == organization && o.Status == OrganizationStatus.Active, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Status == MembershipStatus.Active && (!write || m.Role == OrganizationRole.OrgAdmin || m.Role == OrganizationRole.Analyst), ct))
            throw Error(403, "access_denied");
    }

    public Task<IncidentItem> CreateAsync(Guid actor, Guid organization, IncidentCreate request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, null, "REGISTERED", async () =>
        {
            Fields(request.Title, request.Description);
            await ActiveCategoryAsync(request.CategoryId, ct);
            var entity = new Incident { OrganizationId = organization, CategoryId = request.CategoryId, Title = request.Title.Trim(), Description = request.Description };
            db.Incidents.Add(entity);
            return (entity, null);
        }, ct);

    public Task<IncidentItem> UpdateAsync(Guid actor, Guid organization, Guid incident, IncidentUpdate request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, incident, "UPDATED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Fields(request.Title, request.Description); Reason(request.Reason); Next(entity, request.ExpectedRevision);
            // An inactive historical category remains readable; changing to it is forbidden.
            if (request.CategoryId != entity.CategoryId) await ActiveCategoryAsync(request.CategoryId, ct);
            entity.Title = request.Title.Trim(); entity.Description = request.Description; entity.CategoryId = request.CategoryId;
            return (entity, request.Reason.Trim());
        }, ct);

    public Task<IncidentItem> AssignAsync(Guid actor, Guid organization, Guid incident, IncidentAssignment request, CancellationToken ct = default) =>
        WriteAsync(actor, organization, incident, "ASSIGNED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if (request.AssignedMembershipId is Guid membership && !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization
                && m.Id == membership && m.Status == MembershipStatus.Active && (m.Role == OrganizationRole.OrgAdmin || m.Role == OrganizationRole.Analyst)
                && db.Users.Any(u => u.Id == m.UserId && u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)), ct))
                throw Error(400, "invalid_incident_assignee");
            entity.AssignedMembershipId = request.AssignedMembershipId;
            return (entity, request.Reason.Trim());
        }, ct);

    public Task<IncidentItem> GetAsync(Guid actor, Guid organization, Guid incident, CancellationToken ct = default) =>
        ReadAsync(actor, organization, incident, async () => View(await db.Incidents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.OrganizationId == organization && e.Id == incident, ct) ?? throw Error(404, "incident_not_found")), ct);

    public Task<IncidentPage<IncidentItem>> ListAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(actor, organization, null, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_incident_pagination");
            var rows = await db.Incidents.AsNoTracking().Where(e => e.OrganizationId == organization).OrderBy(e => e.Id)
                .Skip(skip).Take(limit + 1).ToListAsync(ct);
            var more = rows.Count > limit;
            return new IncidentPage<IncidentItem>(rows.Take(limit).Select(View).ToList(), more ? skip + limit : null);
        }, ct);

    public Task<IncidentEventPage> EventsAsync(Guid actor, Guid organization, Guid incident, long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(actor, organization, incident, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_incident_pagination");
            if (!await db.Incidents.AnyAsync(e => e.OrganizationId == organization && e.Id == incident, ct)) throw Error(404, "incident_not_found");
            var rows = await db.IncidentEvents.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident && e.Revision > afterRevision)
                .OrderBy(e => e.Revision).Take(limit + 1).ToListAsync(ct);
            var more = rows.Count > limit;
            var items = rows.Take(limit).Select(e => new IncidentEventItem(e.Id, e.IncidentId, e.Revision, e.ActorUserId, e.CorrelationId,
                e.Action, e.Reason, e.CategoryId, e.Title, e.Description, e.Status, e.AssignedMembershipId, e.OccurredAt, JsonSerializer.Deserialize<JsonElement>(e.DetailsJson), e.ConfidentialityImpact, e.IntegrityImpact, e.AvailabilityImpact)).ToList();
            return new IncidentEventPage(items, more ? items[^1].Revision : null);
        }, ct);

    private async Task<Incident> FindAsync(Guid organization, Guid incident, CancellationToken ct) =>
        await db.Incidents.SingleOrDefaultAsync(e => e.OrganizationId == organization && e.Id == incident, ct) ?? throw Error(404, "incident_not_found");
    private async Task ActiveCategoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.IncidentCategories.AnyAsync(c => c.Id == id && c.IsActive, ct)) throw Error(400, "invalid_incident_category");
    }
    private static void Fields(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || description?.Length > 4000) throw Error(400, "invalid_incident_fields");
    }
    private static void Reason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw Error(400, "incident_reason_required");
    }
    private static void Next(Incident entity, long expected)
    {
        if (entity.Revision != expected || entity.Revision == long.MaxValue) throw Error(409, "stale_incident_revision");
        entity.Revision++;
    }
    private static IncidentItem View(Incident e) => new(e.Id, e.OrganizationId, e.CategoryId, e.Title, e.Description, e.Status, e.AssignedMembershipId, e.Revision, e.CreatedAt, e.ConfidentialityImpact, e.IntegrityImpact, e.AvailabilityImpact);
}
