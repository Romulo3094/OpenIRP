using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Audit;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Audit;

public sealed class AuditReader(OpenIrpDbContext db, AuditRecorder recorder) : IAuditReader
{
    public async Task<AuditPage> ReadOrganizationAsync(Guid actor, Guid organization, long? before = null, int limit = 50, CancellationToken ct = default)
    {
        if (!await ActiveAsync(actor, ct)
            || !await db.Organizations.AnyAsync(o => o.Id == organization && o.Status == OrganizationStatus.Active, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == actor
                && m.Status == MembershipStatus.Active && m.Role == OrganizationRole.OrgAdmin, ct))
        {
            var error = new IdentityOperationException(403, "access_denied");
            await recorder.RejectedAsync(new("audit.organization.read", actor, null, "audit_event"), error);
            throw error;
        }
        return await PageAsync(db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == organization), before, limit, ct);
    }

    public async Task<AuditPage> ReadGlobalAsync(Guid actor, long? before = null, int limit = 50, CancellationToken ct = default)
    {
        if (!await ActiveAsync(actor, ct) || !await db.PlatformAdministrators.AnyAsync(p => p.UserId == actor && p.RevokedAt == null, ct))
        {
            var error = new IdentityOperationException(403, "access_denied");
            await recorder.RejectedAsync(new("audit.global.read", actor, null, "audit_event"), error);
            throw error;
        }
        // Global audit access never grants access to tenant events.
        return await PageAsync(db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == null), before, limit, ct);
    }

    private Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == actor && u.IsActive
        && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct);

    private static async Task<AuditPage> PageAsync(IQueryable<AuditEvent> query, long? before, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100 || before is <= 0) throw new IdentityOperationException(400, "invalid_audit_pagination");
        var rows = await query.Where(e => before == null || e.Id < before).OrderByDescending(e => e.Id).Take(limit + 1)
            .Select(e => new AuditEventView(e.Id, e.OrganizationId, e.ActorUserId, e.ActorType, e.Action, e.ResourceType,
                e.ResourceId, e.Outcome, e.CorrelationId, e.DetailsJson, e.OccurredAt)).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new AuditPage(rows, more ? rows[^1].Id : null);
    }
}
