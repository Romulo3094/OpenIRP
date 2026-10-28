using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task<T> ExecutionRead<T>(Guid actor, Guid organization, Guid incident, Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await AuthorizeAsync(actor, organization, false, ct);
            await FindAsync(organization, incident, ct);
            var result = await operation();
            await tx.CommitAsync(ct); return result;
        }
        catch (IdentityOperationException e)
        { await audit.RejectedAsync(new("execution.read", actor, organization, "incident", incident), e); throw; }
    }

    public Task<ExecutionPage> ListAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ExecutionRead(actor, organization, incident, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_execution_pagination");
            var rows = await db.PlaybookExecutions.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident)
                .OrderBy(e => e.Id).Skip(skip).Take(limit + 1).Select(e => new ExecutionSummary(e.Id, e.VersionId, e.Status, e.Revision, e.CreatedAt)).ToListAsync(ct);
            return new ExecutionPage(rows.Take(limit).ToList(), rows.Count > limit ? skip + limit : null);
        }, ct);
    public Task<ExecutionItem> GetAsync(Guid actor, Guid organization, Guid incident, Guid execution, CancellationToken ct = default) =>
        ExecutionRead(actor, organization, incident, async () =>
        {
            var entity = await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == execution
                && e.OrganizationId == organization && e.IncidentId == incident, ct) ?? throw Error(404, "execution_not_found");
            var revision = await db.Incidents.Where(i => i.Id == incident && i.OrganizationId == organization).Select(i => i.Revision).SingleAsync(ct);
            return await ExecutionView(entity, revision, ct);
        }, ct);
    public Task<IReadOnlyList<ExecutionEventItem>> EventsAsync(Guid actor, Guid organization, Guid incident, Guid execution,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default) =>
        ExecutionRead<IReadOnlyList<ExecutionEventItem>>(actor, organization, incident, async () =>
        {
            if (afterRevision < 0 || limit is < 1 or > 100) throw Error(400, "invalid_execution_pagination");
            if (!await db.PlaybookExecutions.AnyAsync(e => e.Id == execution && e.OrganizationId == organization && e.IncidentId == incident, ct))
                throw Error(404, "execution_not_found");
            return await db.ExecutionTaskEvents.AsNoTracking().Where(e => e.ExecutionId == execution && e.OrganizationId == organization
                && e.IncidentId == incident && e.Revision > afterRevision).OrderBy(e => e.Revision).Take(limit)
                .Select(e => new ExecutionEventItem(e.Id, e.Revision, e.IncidentRevision, e.TaskId, e.Action, e.ExecutionStatus, e.TaskStatus,
                    e.AssignedMembershipId, e.ActorUserId, e.CorrelationId, e.Reason, e.OccurredAt)).ToListAsync(ct);
        }, ct);
}
