using System.Text.Json;
using OpenIRP.Infrastructure.Audit;
using System.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Identity;

// All privileged writes in this slice participate in the same database transaction gate.
// It also coordinates account deactivation with membership changes across organizations.
// Never share a scoped instance / DbContext between simultaneous operations.
public sealed class IdentityAdministration(OpenIrpDbContext db, UserManager<ApplicationUser> users, AuditRecorder recorder) : IIdentityAdministration
{
    private static IdentityOperationException Error(int status, string code) => new(status, code);

    private async Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean administration scope.");
        db.ChangeTracker.Clear(); // Authorization must use current persisted state after the lock.
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    private async Task<T> WriteAsync<T>(AuditAttempt attempt, Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            await using var tx = await BeginAsync(ct);
            var result = await operation();
            var details = "{}";
            var successful = attempt;
            if (result is OrganizationView org)
                successful = attempt with { Organization = org.Id, ResourceId = org.Id };
            else if (result is MembershipView member)
            {
                successful = attempt with { Organization = member.OrganizationId, ResourceId = member.Id };
                details = JsonSerializer.Serialize(new { userId = member.UserId, role = member.Role.ToString(), status = member.Status.ToString() });
            }
            else if (result is Guid id)
                successful = attempt with { ResourceId = id };
            db.AuditEvents.Add(recorder.Success(successful, details));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception e)
        {
            Exception effective = e switch
            {
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => Error(409, "duplicate_identity_or_slug"),
                PostgresException { SqlState: "55P03" or "40P01" or "40001" } => Error(409, "concurrent_change_retry"),
                DbUpdateException { InnerException: PostgresException { SqlState: "55P03" or "40P01" or "40001" } } => Error(409, "concurrent_change_retry"),
                _ => e
            };
            // The await-using transaction has already rolled back before the independent audit write.
            await recorder.RejectedAsync(attempt, effective);
            if (ReferenceEquals(e, effective)) throw;
            throw effective;
        }
        finally { db.ChangeTracker.Clear(); }
    }
    public Task<Guid> BootstrapAsync(NewAccount account, CancellationToken ct = default) => WriteAsync(new("platform.bootstrap", null, null, "platform_administrator"), async () =>
    {
        // Any historical grant closes bootstrap forever, including a revoked grant.
        if (await db.PlatformAdministrators.AnyAsync(ct)) throw Error(409, "bootstrap_already_completed");
        var user = await CreateAccountAsync(account);
        db.PlatformAdministrators.Add(new PlatformAdministrator { UserId = user.Id });
        return user.Id;
    }, ct);

    private async Task<ApplicationUser> CreateAccountAsync(NewAccount account)
    {
        if (account is null || string.IsNullOrWhiteSpace(account.Email) || account.Email.Length > 254
            || !MailAddress.TryCreate(account.Email, out var address) || address.Address != account.Email
            || string.IsNullOrEmpty(account.Password) || account.Password.Length > 128)
            throw Error(400, "invalid_account");
        // Never silently promote, reset, or take over an existing identity.
        if (await users.FindByEmailAsync(account.Email) is not null) throw Error(409, "account_already_exists");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = account.Email, Email = account.Email,
            EmailConfirmed = true, LockoutEnabled = true };
        // EmailConfirmed means the local operator has verified the recipient out of band.
        var result = await users.CreateAsync(user, account.Password);
        if (!result.Succeeded) throw Error(400, "account_rejected:" + string.Join(',', result.Errors.Select(e => e.Code)));
        return user;
    }

    private async Task RequireActiveAsync(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct)) throw Error(403, "access_denied");
    }

    private async Task RequirePlatformAsync(Guid actor, CancellationToken ct)
    {
        await RequireActiveAsync(actor, ct);
        if (!await db.PlatformAdministrators.AnyAsync(p => p.UserId == actor && p.RevokedAt == null, ct))
            throw Error(403, "access_denied");
    }

    private async Task RequireOrganizationAsync(Guid actor, Guid org, bool admin, CancellationToken ct)
    {
        await RequireActiveAsync(actor, ct);
        // PLATFORM_ADMIN deliberately has no implicit tenant access.
        if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == org && m.UserId == actor
            && m.Status == MembershipStatus.Active && (!admin || m.Role == OrganizationRole.OrgAdmin), ct)
            || !await db.Organizations.AnyAsync(o => o.Id == org && o.Status == OrganizationStatus.Active, ct))
            throw Error(403, "access_denied");
    }

    public Task<OrganizationView> CreateOrganizationAsync(Guid actor, CreateOrganization request, CancellationToken ct = default) => WriteAsync(new("organization.create", actor, null, "organization"), async () =>
    {
        await RequirePlatformAsync(actor, ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Slug is null
            || request.Slug.Length > 100 || !Regex.IsMatch(request.Slug, "^[a-z0-9]+(-[a-z0-9]+)*$")
            || request.CountryCode is null || !Regex.IsMatch(request.CountryCode, "^[A-Z]{2}$"))
            throw Error(400, "invalid_organization");
        var org = new Organization { Name = request.Name.Trim(), Slug = request.Slug,
            CountryCode = request.CountryCode, CreatedByUserId = actor };
        db.Organizations.Add(org);
        // Flush inside the same transaction: a rejected account must roll back the organization too.
        await db.SaveChangesAsync(ct);
        var user = await CreateAccountAsync(request.FirstAdministrator);
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org.Id,
            UserId = user.Id, Role = OrganizationRole.OrgAdmin });
        return View(org);
    }, ct);

    public async Task<IReadOnlyList<OrganizationView>> ListOrganizationsAsync(Guid actor, CancellationToken ct = default)
    {
        await RequireActiveAsync(actor, ct);
        return await db.Organizations.AsNoTracking().Where(o => o.Status == OrganizationStatus.Active
            && db.OrganizationMemberships.Any(m => m.OrganizationId == o.Id && m.UserId == actor
                && m.Status == MembershipStatus.Active))
            .Select(o => new OrganizationView(o.Id, o.Name, o.Slug, o.CountryCode)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MembershipView>> ListMembersAsync(Guid actor, Guid organization, CancellationToken ct = default)
    {
        try { await RequireOrganizationAsync(actor, organization, true, ct); }
        catch (IdentityOperationException e)
        {
            await recorder.RejectedAsync(new("membership.list", actor, null, "membership"), e);
            throw;
        }
        return await db.OrganizationMemberships.AsNoTracking().Where(m => m.OrganizationId == organization)
            .Select(m => new MembershipView(m.Id, m.OrganizationId, m.UserId, m.Role, m.Status, m.UpdatedAt)).ToListAsync(ct);
    }

    public Task<MembershipView> AddMemberAsync(Guid actor, Guid organization, AddMember request, CancellationToken ct = default) => WriteAsync(new("membership.add", actor, organization, "membership"), async () =>
    {
        await RequireOrganizationAsync(actor, organization, true, ct);
        if (!Enum.IsDefined(request.Role)) throw Error(400, "invalid_role");
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId && u.IsActive, ct)) throw Error(400, "invalid_user");
        var member = new OrganizationMembership { OrganizationId = organization, UserId = request.UserId, Role = request.Role };
        db.OrganizationMemberships.Add(member);
        return View(member);
    }, ct);

    public Task<MembershipView> ChangeMemberAsync(Guid actor, Guid organization, Guid membership, ChangeMember request, CancellationToken ct = default) => WriteAsync(new("membership.change", actor, organization, "membership", membership), async () =>
    {
        await RequireOrganizationAsync(actor, organization, true, ct);
        if (!Enum.IsDefined(request.Role) || !Enum.IsDefined(request.Status)) throw Error(400, "invalid_membership");
        var member = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.Id == membership && m.OrganizationId == organization, ct)
            ?? throw Error(404, "membership_not_found");
        if (member.UpdatedAt != request.ExpectedUpdatedAt) throw Error(409, "stale_membership");
        member.Role = request.Role;
        member.Status = request.Status;
        member.RevokedAt = request.Status == MembershipStatus.Revoked ? DateTimeOffset.UtcNow : null;
        // PostgreSQL stores microseconds. Truncate so response tokens round-trip exactly.
        member.UpdatedAt = NextRevision(member.UpdatedAt);
        await db.SaveChangesAsync(ct);
        await RequireRemainingAdminAsync(organization, ct);
        return View(member);
    }, ct);

    private static DateTimeOffset NextRevision(DateTimeOffset? previous)
    {
        var ticks = DateTimeOffset.UtcNow.Ticks / 10 * 10;
        return new DateTimeOffset(Math.Max(ticks, (previous?.Ticks ?? 0) + 10), TimeSpan.Zero);
    }

    private async Task RequireRemainingAdminAsync(Guid org, CancellationToken ct)
    {
        if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == org && m.Role == OrganizationRole.OrgAdmin
            && m.Status == MembershipStatus.Active && db.Users.Any(u => u.Id == m.UserId && u.IsActive), ct))
            throw Error(409, "last_active_organization_administrator");
    }

    public async Task SetUserActiveAsync(Guid actor, Guid user, bool active, CancellationToken ct = default)
    {
        await WriteAsync(new(active ? "user.activate" : "user.deactivate", actor, null, "user", user), async () =>
        {
            await RequirePlatformAsync(actor, ct);
            var target = await db.Users.SingleOrDefaultAsync(u => u.Id == user, ct) ?? throw Error(404, "user_not_found");
            if (!active && await db.PlatformAdministrators.AnyAsync(p => p.UserId == user && p.RevokedAt == null, ct))
                throw Error(409, "platform_administrator_deactivation_not_supported");
            target.IsActive = active;
            target.SecurityStamp = Guid.NewGuid().ToString();
            await db.SaveChangesAsync(ct);
            foreach (var org in await db.OrganizationMemberships.Where(m => m.UserId == user)
                .Select(m => m.OrganizationId).Distinct().ToListAsync(ct))
                await RequireRemainingAdminAsync(org, ct);
            return true;
        }, ct);
    }

    private static OrganizationView View(Organization o) => new(o.Id, o.Name, o.Slug, o.CountryCode);
    private static MembershipView View(OrganizationMembership m) => new(m.Id, m.OrganizationId, m.UserId, m.Role, m.Status, m.UpdatedAt);
}
