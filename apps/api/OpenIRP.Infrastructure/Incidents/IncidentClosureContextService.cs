using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private const string ClosureContextPrefix = "closure-v1:";

    private async Task CheckClosureContext(Guid organization, Guid incident, string? expected, CancellationToken ct)
    {
        // Optional for compatibility. A context token is a concurrency precondition, never authorization or risk acceptance.
        if (expected is null) return;
        if (!expected.StartsWith(ClosureContextPrefix, StringComparison.Ordinal) || expected.Length != ClosureContextPrefix.Length + 64
            || expected.Skip(ClosureContextPrefix.Length).Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error(400, "invalid_closure_context");
        // All supported writers use WriteAsync's transaction gate, including withdrawals and file recovery.
        if (!string.Equals(expected, await BuildClosureContext(organization, incident, DateTimeOffset.UtcNow, ct), StringComparison.Ordinal))
            throw Error(409, "stale_closure_context");
    }

    private static string ClosureContextDetails(string? token) => token is null ? "{}"
        : JsonSerializer.Serialize(new { closureContextToken = token }, DetailsOptions);

    private static async Task HashClosureRows<T>(IncrementalHash hash, string section, IQueryable<T> rows, CancellationToken ct)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(section + "\n"));
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct))
        {
            // JSON escapes embedded newlines. The section and row boundaries are unambiguous, including empty sections.
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(row));
            hash.AppendData("\n"u8);
        }
    }

    private async Task<string> BuildClosureContext(Guid organization, Guid incident, DateTimeOffset checkedAt, CancellationToken ct)
    {
        // Protocol v1: stable, ordered, metadata-only projections streamed into SHA-256.
        // Changing a projection or its serialization requires a new protocol prefix.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(ClosureContextPrefix + "\n"));
        var parent = db.Incidents.AsNoTracking().Where(i => i.OrganizationId == organization && i.Id == incident);
        var runs = db.PlaybookExecutions.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident);
        var versions = runs.Select(e => e.VersionId);
        await HashClosureRows(hash, "incident", parent.Select(i => new { i.Id, i.OrganizationId, i.Revision }), ct);
        await HashClosureRows(hash, "organization", db.Organizations.AsNoTracking().Where(o => o.Id == organization)
            .Select(o => new { o.Id, o.Status, o.UpdatedAt }), ct);
        await HashClosureRows(hash, "members", (from m in db.OrganizationMemberships.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.OrganizationId == organization
            orderby m.Id
            select new { m.Id, m.UserId, m.Role, m.Status, m.UpdatedAt, u.IsActive, u.LockoutEnd,
                Unlocked = u.LockoutEnd == null || u.LockoutEnd <= checkedAt }), ct);
        await HashClosureRows(hash, "category", db.IncidentCategories.AsNoTracking().Where(c => parent.Any(i => i.CategoryId == c.Id))
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.Revision, c.IsActive }), ct);
        await HashClosureRows(hash, "executions", runs.OrderBy(e => e.Id).Select(e => new { e.Id, e.VersionId, e.Revision, e.Status }), ct);
        await HashClosureRows(hash, "tasks", db.ExecutionTasks.AsNoTracking().Where(t => t.OrganizationId == organization && t.IncidentId == incident)
            .OrderBy(t => t.Id).Select(t => new { t.Id, t.ExecutionId, t.Status, t.AssignedMembershipId, t.CompletionEvidenceId, t.ChangedAtRevision }), ct);
        await HashClosureRows(hash, "versions", db.PlaybookVersions.AsNoTracking().Where(v => versions.Contains(v.Id))
            .OrderBy(v => v.Id).Select(v => new { v.Id, v.Status }), ct);
        var ancestors = await db.Database.SqlQuery<Guid>($"""SELECT DISTINCT a.id AS "Value" FROM app.playbook_executions e CROSS JOIN LATERAL app.security_ancestors(e.version_id) a WHERE e.organization_id={organization} AND e.incident_id={incident} """).ToListAsync(ct);
        await HashClosureRows(hash, "holds", db.PlaybookSecurityHolds.AsNoTracking().Where(h => ancestors.Contains(h.VersionId))
            .OrderBy(h => h.Id).Select(h => new { h.Id, h.VersionId, h.Revision }), ct);
        await HashClosureRows(hash, "security-decisions", db.ExecutionSecurityDecisions.AsNoTracking().Where(d => d.OrganizationId == organization && d.IncidentId == incident)
            .OrderBy(d => d.Id).Select(d => new { d.Id }), ct);
        await HashClosureRows(hash, "evidence", db.IncidentEvidence.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident)
            .OrderBy(e => e.Id).Select(e => new { e.Id, e.Revision, e.Status, e.ExecutionId, e.TaskId, e.Kind, e.Classification }), ct);
        await HashClosureRows(hash, "files", db.EvidenceFiles.AsNoTracking().Where(f => f.OrganizationId == organization && f.IncidentId == incident)
            .OrderBy(f => f.EvidenceId).Select(f => new { f.EvidenceId, f.Revision, f.State, f.Length, f.Sha256 }), ct);
        await HashClosureRows(hash, "step-approvals", db.StepApprovals.AsNoTracking().Where(d => d.OrganizationId == organization && d.IncidentId == incident)
            .OrderBy(d => d.Id).Select(d => new { d.Id }), ct);
        await HashClosureRows(hash, "skips", db.SkipAuthorizations.AsNoTracking().Where(d => d.OrganizationId == organization && d.IncidentId == incident)
            .OrderBy(d => d.Id).Select(d => new { d.Id }), ct);
        await HashClosureRows(hash, "dependency-exceptions", db.DependencyExceptions.AsNoTracking().Where(d => d.OrganizationId == organization && d.IncidentId == incident)
            .OrderBy(d => d.Id).Select(d => new { d.Id }), ct);
        await HashClosureRows(hash, "dependency-revocations", db.DependencyExceptionRevocations.AsNoTracking().Where(d => d.OrganizationId == organization && d.IncidentId == incident)
            .OrderBy(d => d.Id).Select(d => new { d.Id }), ct);
        await HashClosureRows(hash, "incident-capabilities", db.IncidentCapabilityAssessments.AsNoTracking().Where(c => c.OrganizationId == organization && c.IncidentId == incident)
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.CapabilityId, c.AssessedAtRevision }), ct);
        var profiles = db.OrganizationCapabilities.AsNoTracking().Where(c => c.OrganizationId == organization);
        await HashClosureRows(hash, "organization-capabilities", profiles.OrderBy(c => c.Id).Select(c => new { c.Id, c.CapabilityId, c.Revision }), ct);
        await HashClosureRows(hash, "capability-catalog", db.CapabilityCatalog.AsNoTracking().Where(c => profiles.Any(p => p.CapabilityId == c.Id)
            || db.IncidentCapabilityAssessments.Any(a => a.OrganizationId == organization && a.IncidentId == incident && a.CapabilityId == c.Id)
            || db.PlaybookCapabilityRequirements.Any(r => versions.Contains(r.VersionId) && r.CapabilityId == c.Id))
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.Revision, c.IsActive }), ct);
        return ClosureContextPrefix + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
