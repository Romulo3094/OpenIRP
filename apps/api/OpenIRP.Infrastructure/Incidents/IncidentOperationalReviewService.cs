using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;
namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private Task<string> OperationalContext(Guid org,Guid inc,CancellationToken ct) => db.Database
        .SqlQuery<string>($"""SELECT app.operational_context({org},{inc}) AS "Value" """).SingleAsync(ct);
    private async Task OperationalAdmin(Guid actor,Guid org,CancellationToken ct)
    {
        if(!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId==org && m.UserId==actor
            && m.Status==MembershipStatus.Active && m.Role==OrganizationRole.OrgAdmin,ct)) throw Error(403,"organization_admin_required");
    }
    private static OperationalReviewItem OperationView(OperationalReview r,bool current) => new(r.Id,r.TaskId,r.EvidenceId,r.Kind,r.Accepted,
        r.Method,r.Assessment,r.AlternativeProcedure,r.ContextToken,r.IncidentRevision,r.ActorUserId,r.CorrelationId,r.Reason,
        r.SelfReview,r.SelfReviewJustification,r.OccurredAt,current);
    public async Task<OperationalReviewItem> ReviewOperationAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,
        RegisterOperationalReview request,CancellationToken ct=default)
    {
        OperationalReview? row=null;
        await WriteAsync(actor,organization,incident,"OPERATIONAL_REVIEWED",async () =>
        {
            await OperationalAdmin(actor,organization,ct);
            var parent=await FindAsync(organization,incident,ct); Reason(request.Reason);
            if(!Enum.IsDefined(request.Kind)) throw Error(400,"invalid_operational_review_kind");
            static bool Invalid(string? s,int max) => string.IsNullOrWhiteSpace(s) || s.Length>max || s.Contains('\0');
            if(Invalid(request.Method,2000) || Invalid(request.Assessment,4000) || request.Reason.Contains('\0')
                || request.SelfReviewJustification?.Contains('\0')==true) throw Error(400,"invalid_operational_review_text");
            if(request.Kind==OperationalReviewKind.Alternative ? Invalid(request.AlternativeProcedure,4000) : request.AlternativeProcedure is not null)
                throw Error(400,"invalid_alternative_procedure");
            var run=await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId==organization && e.IncidentId==incident && e.Id==execution,ct)
                ?? throw Error(404,"execution_not_found");
            var item=await db.ExecutionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.OrganizationId==organization && t.IncidentId==incident && t.ExecutionId==run.Id && t.Id==task,ct)
                ?? throw Error(404,"execution_task_not_found");
            var evidence=await FindEvidence(organization,incident,request.EvidenceId,ct);
            if(evidence.Status!="ACTIVE" || evidence.TaskId!=task || evidence.ExecutionId!=execution || evidence.VersionId!=run.VersionId)
                throw Error(409,"operational_evidence_scope_invalid");
            if(request.Kind==OperationalReviewKind.Evidence && (item.Status!="COMPLETED" || item.CompletionEvidenceId!=evidence.Id))
                throw Error(409,"completion_evidence_review_required");
            var token=await OperationalContext(organization,incident,ct);
            if(request.ExpectedContext!=token) throw Error(409,"stale_operational_context");
            var own=await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId==organization && m.UserId==actor
                && (m.Id==item.AssignedMembershipId || m.Id==parent.AssignedMembershipId),ct);
            if(request.SelfReview!=own || request.SelfReviewJustification?.Length>2000 || (own && string.IsNullOrWhiteSpace(request.SelfReviewJustification))
                || (!own && request.SelfReviewJustification is not null)) throw Error(400,"invalid_operational_self_review");
            if(request.Accepted) await VerifyCompletionFile(actor,organization,incident,evidence.Id,ct);
            Next(parent,request.ExpectedIncidentRevision);
            row=new() { OrganizationId=organization,IncidentId=incident,ExecutionId=execution,TaskId=task,EvidenceId=evidence.Id,
                Kind=request.Kind.ToString().ToUpperInvariant(),Accepted=request.Accepted,Method=request.Method.Trim(),Assessment=request.Assessment.Trim(),
                AlternativeProcedure=request.AlternativeProcedure?.Trim(),ContextToken=token,IncidentRevision=parent.Revision,
                ActorUserId=actor,CorrelationId=context.CorrelationId,Reason=request.Reason.Trim(),SelfReview=own,
                SelfReviewJustification=request.SelfReviewJustification?.Trim(),OccurredAt=EvidenceNow() };
            db.OperationalReviews.Add(row);return(parent,request.Reason.Trim());
        },ct,()=>JsonSerializer.Serialize(new {operationalReviewId=row!.Id,row.TaskId,row.Kind,row.Accepted},DetailsOptions));
        return OperationView(row!,true);
    }
    public Task<OperationalReviewPage> OperationalReviewsAsync(Guid actor,Guid organization,Guid incident,Guid execution,
        long afterRevision=0,int limit=50,CancellationToken ct=default) => ExecutionRead(actor,organization,incident,async () =>
    {
        await OperationalAdmin(actor,organization,ct);
        if(afterRevision<0 || limit is <1 or >100) throw Error(400,"invalid_operational_pagination");
        if(!await db.PlaybookExecutions.AnyAsync(e => e.OrganizationId==organization && e.IncidentId==incident && e.Id==execution,ct)) throw Error(404,"execution_not_found");
        var token=await OperationalContext(organization,incident,ct);
        var query=db.OperationalReviews.AsNoTracking().Where(r => r.OrganizationId==organization && r.IncidentId==incident && r.ExecutionId==execution);
        var rows=await query.Where(r => r.IncidentRevision>afterRevision).OrderBy(r => r.IncidentRevision).Take(limit+1).ToListAsync(ct);
        var currentIds=await query.Where(r => r.ContextToken==token && !query.Any(n => n.TaskId==r.TaskId && n.Kind==r.Kind && n.IncidentRevision>r.IncidentRevision)
            && db.OrganizationMemberships.Any(m => m.OrganizationId==organization && m.UserId==r.ActorUserId && m.Role==OrganizationRole.OrgAdmin && m.Status==MembershipStatus.Active
            && db.Users.Any(u => u.Id==m.UserId && u.IsActive && (u.LockoutEnd==null || u.LockoutEnd<=DateTimeOffset.UtcNow))))
            .Select(r => r.Id).ToListAsync(ct);
        var ids=currentIds.ToHashSet();
        return new OperationalReviewPage(token,rows.Take(limit).Select(r => OperationView(r,ids.Contains(r.Id))).ToList(),rows.Count>limit ? rows[limit-1].IncidentRevision : null);
    },ct);
}
