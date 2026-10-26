using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIRP.Application.Audit;
using OpenIRP.Infrastructure.Audit;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private static DateTimeOffset EvidenceNow()
    { var now = DateTimeOffset.UtcNow; return now.AddTicks(-(now.Ticks % 10)); }
    private void EvidenceEvent(IncidentEvidence e, Guid actor, string action, string reason) =>
        db.EvidenceEvents.Add(new() { OrganizationId=e.OrganizationId, IncidentId=e.IncidentId, EvidenceId=e.Id,
            Revision=e.Revision, Action=action, ActorUserId=actor, CorrelationId=context.CorrelationId, Reason=reason.Trim(), OccurredAt=EvidenceNow() });
    private static EvidenceItem EvidenceView(IncidentEvidence e, bool content = true) =>
        new(e.Id,e.OrganizationId,e.IncidentId,e.ExecutionId,e.TaskId,e.Kind,e.Classification,e.Status,e.Revision,
            e.Status=="ACTIVE" ? e.Title : null, content && e.Status=="ACTIVE" && e.Kind!="FILE" ? e.Content : null,
            e.CreatedByUserId,e.CreatedAt,e.WithdrawnByUserId,e.WithdrawnAt,e.WithdrawalReason);
    private async Task<IncidentEvidence> FindEvidence(Guid organization, Guid incident, Guid id, CancellationToken ct) =>
        await db.IncidentEvidence.SingleOrDefaultAsync(e=>e.OrganizationId==organization && e.IncidentId==incident && e.Id==id,ct)
            ?? throw Error(404,"evidence_not_found");

    public async Task<EvidenceResult> RegisterAsync(Guid actor, Guid organization, Guid incident, RegisterEvidence request, CancellationToken ct = default)
    {
        IncidentEvidence? row=null;
        var receipt=await WriteAsync(actor,organization,incident,"EVIDENCE_REGISTERED",async () =>
        {
            var entity=await FindAsync(organization,incident,ct);
            Reason(request.Reason); Next(entity,request.ExpectedIncidentRevision);
            if(!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Classification)) throw Error(400,"invalid_evidence_type_or_classification");
            if(request.Kind==EvidenceKind.File) throw Error(409,"file_evidence_not_available");
            if(request.Classification==EvidenceClassification.Restricted) throw Error(409,"restricted_evidence_policy_pending");
            if(string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>200 || string.IsNullOrWhiteSpace(request.Content) || request.Content.Length>16000
                || request.Content.Contains('\0') || request.Title.Contains('\0')) throw Error(400,"invalid_evidence_content");
            var body=request.Content.Trim();
            if(request.Kind==EvidenceKind.Reference && (body.Length>2048 || body.Any(char.IsControl)
                || !Uri.TryCreate(body,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps
                || !uri.IsWellFormedOriginalString() || uri.UserInfo.Length!=0)) throw Error(400,"invalid_evidence_reference");
            if((request.TaskId is null)!=(request.ExecutionId is null)) throw Error(400,"incomplete_evidence_task_scope");
            Guid? version=null;
            if(request.TaskId is Guid task)
            {
                var item=await db.ExecutionTasks.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==task && t.ExecutionId==request.ExecutionId
                    && t.OrganizationId==organization && t.IncidentId==incident,ct) ?? throw Error(404,"execution_task_not_found");
                if(entity.Status!="IN_RESPONSE" || item.Status=="COMPLETED" ||
                    !await db.PlaybookExecutions.AnyAsync(e=>e.Id==item.ExecutionId && e.Status=="IN_PROGRESS",ct)) throw Error(409,"evidence_task_not_active");
                version=item.VersionId;
            }
            row=new() { OrganizationId=organization,IncidentId=incident,ExecutionId=request.ExecutionId,TaskId=request.TaskId,VersionId=version,
                Kind=request.Kind.ToString().ToUpperInvariant(),Classification=request.Classification.ToString().ToUpperInvariant(),
                Title=request.Title.Trim(),Content=body,CreatedByUserId=actor,CreatedAt=EvidenceNow() };
            // Normalize the scheme without fetching the reference.
            if(row.Kind=="REFERENCE") row.Content="https"+row.Content[5..];
            db.IncidentEvidence.Add(row); EvidenceEvent(row,actor,"REGISTERED",request.Reason);
            return (entity,request.Reason.Trim());
        },ct,()=>JsonSerializer.Serialize(new { evidenceId=row!.Id, row.TaskId, row.Kind, row.Classification }));
        return new(EvidenceView(row!),receipt.Revision);
    }

    public async Task<EvidenceResult> WithdrawAsync(Guid actor, Guid organization, Guid incident, Guid evidence, WithdrawEvidence request, CancellationToken ct = default)
    {
        var attempt=new AuditAttempt("evidence.withdraw",actor,organization,"evidence",evidence);
        try
        {
            if(db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean evidence scope.");
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'",ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)",ct);
            await AuthorizeAsync(actor,organization,true,ct);
            if(!await db.OrganizationMemberships.AnyAsync(m=>m.OrganizationId==organization && m.UserId==actor
                && m.Status==MembershipStatus.Active && m.Role==OrganizationRole.OrgAdmin,ct)) throw Error(403,"organization_admin_required");
            var parent=await FindAsync(organization,incident,ct); Reason(request.Reason);
            var row=await FindEvidence(organization,incident,evidence,ct);
            if(row.Revision!=request.ExpectedEvidenceRevision) throw Error(409,"stale_evidence_revision");
            if(row.Status!="ACTIVE") throw Error(409,"evidence_already_withdrawn");
            row.Status="WITHDRAWN"; row.Revision=2; row.WithdrawnByUserId=actor; row.WithdrawnAt=EvidenceNow(); row.WithdrawalReason=request.Reason.Trim();
            await QuarantineWithdrawnFile(row,actor,request.Reason,ct);
            EvidenceEvent(row,actor,"WITHDRAWN",request.Reason);
            db.AuditEvents.Add(audit.Success(attempt,JsonSerializer.Serialize(new { row.IncidentId,row.Revision })));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(EvidenceView(row),parent.Revision);
        }
        catch(Exception e)
        {
            Exception effective=e switch {
                DbUpdateConcurrencyException => Error(409,"concurrent_evidence_change"),
                DbUpdateException { InnerException: PostgresException { SqlState:"23505" } } => Error(409,"concurrent_evidence_change"),
                PostgresException { SqlState:"55P03" or "40P01" or "40001" } => Error(409,"concurrent_change_retry"),
                DbUpdateException { InnerException: PostgresException { SqlState:"55P03" or "40P01" or "40001" } } => Error(409,"concurrent_change_retry"),
                _=>e };
            await audit.RejectedAsync(attempt,effective);
            if(ReferenceEquals(e,effective)) throw;
            throw effective;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<T> EvidenceRead<T>(Guid actor, Guid organization, Guid incident, Func<Task<T>> read, CancellationToken ct)
    {
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
            await AuthorizeAsync(actor,organization,false,ct); await FindAsync(organization,incident,ct);
            var result=await read(); await tx.CommitAsync(ct); return result;
        }
        catch(IdentityOperationException e)
        { await audit.RejectedAsync(new("evidence.read",actor,organization,"incident",incident),e); throw; }
    }
    public Task<EvidenceItem> GetEvidenceAsync(Guid actor,Guid organization,Guid incident,Guid evidence,CancellationToken ct=default) =>
        EvidenceRead(actor,organization,incident,async()=>EvidenceView(await FindEvidence(organization,incident,evidence,ct)),ct);
    public Task<EvidencePage> ListEvidenceAsync(Guid actor,Guid organization,Guid incident,int skip=0,int limit=50,CancellationToken ct=default) =>
        EvidenceRead(actor,organization,incident,async()=>
        {
            if(skip is <0 or >1000000 || limit is <1 or >100) throw Error(400,"invalid_evidence_pagination");
            var rows=await db.IncidentEvidence.AsNoTracking().Where(e=>e.OrganizationId==organization && e.IncidentId==incident)
                .OrderBy(e=>e.Id).Skip(skip).Take(limit+1).ToListAsync(ct);
            return new EvidencePage(rows.Take(limit).Select(e=>EvidenceView(e,false)).ToList(),rows.Count>limit ? skip+limit : null);
        },ct);
    public Task<IReadOnlyList<EvidenceEventItem>> EvidenceEventsAsync(Guid actor,Guid organization,Guid incident,Guid evidence,long afterRevision=0,int limit=50,CancellationToken ct=default) =>
        EvidenceRead<IReadOnlyList<EvidenceEventItem>>(actor,organization,incident,async()=>
        {
            if(afterRevision<0 || limit is <1 or >100) throw Error(400,"invalid_evidence_pagination");
            await FindEvidence(organization,incident,evidence,ct);
            return await db.EvidenceEvents.AsNoTracking().Where(e=>e.OrganizationId==organization && e.IncidentId==incident && e.EvidenceId==evidence && e.Revision>afterRevision)
                .OrderBy(e=>e.Revision).Take(limit).Select(e=>new EvidenceEventItem(e.Id,e.Revision,e.Action,e.ActorUserId,e.CorrelationId,e.Reason,e.OccurredAt)).ToListAsync(ct);
        },ct);
}
