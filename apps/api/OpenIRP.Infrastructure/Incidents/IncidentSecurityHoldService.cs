using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;
namespace OpenIRP.Infrastructure.Incidents;
public sealed partial class IncidentService
{
    private async Task<PlaybookSecurityHold?> EffectiveHold(Guid version,DateTimeOffset? at,CancellationToken ct)
    {
        var id=await db.Database.SqlQuery<Guid?>($"""SELECT app.effective_security_hold({version},{at}) AS "Value" """).SingleAsync(ct);
        return id is null ? null : await db.PlaybookSecurityHolds.AsNoTracking().SingleAsync(h=>h.Id==id,ct);
    }
    private static SecurityHoldItem VisibleHold(PlaybookSecurityHold hold,Guid version) => hold.VersionId==version
        ? new(hold.Id,version,hold.Revision,hold.ActorUserId,hold.Reason,hold.OccurredAt)
        : new(hold.Id,version,0,Guid.Empty,"Aviso de seguridad heredado de una fuente; requiere evaluacion local independiente",hold.OccurredAt,true);
    public Task<ExecutionSecurityState> SecurityStateAsync(Guid actor,Guid organization,Guid incident,Guid execution,CancellationToken ct=default)=>
        ExecutionRead(actor,organization,incident,async()=>
        {
            var run=await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==execution&&e.OrganizationId==organization&&e.IncidentId==incident,ct)??throw Error(404,"execution_not_found");
            var parent=await db.Incidents.AsNoTracking().SingleAsync(i=>i.Id==incident&&i.OrganizationId==organization,ct);
            var version=await db.PlaybookVersions.AsNoTracking().SingleAsync(v=>v.Id==run.VersionId,ct);
            var hold=await EffectiveHold(run.VersionId,run.FinishedAt,ct);
            var decisions=hold is null?new List<ExecutionSecurityDecision>():await db.ExecutionSecurityDecisions.AsNoTracking().Where(d=>d.OrganizationId==organization&&d.IncidentId==incident&&d.ExecutionId==execution&&d.SecurityHoldId==hold.Id).OrderBy(d=>d.ExecutionRevision).ToListAsync(ct);
            return new ExecutionSecurityState(run.Id,run.VersionId,run.Status,version.Status,run.Revision,parent.Revision,
                hold is null?null:VisibleHold(hold,run.VersionId),
                decisions.Select(d=>new SecurityDecisionItem(d.Id,d.SecurityHoldId,d.Action,d.ExecutionRevision,d.IncidentRevision,d.ActorUserId,d.CorrelationId,d.Reason,d.OccurredAt)).ToList(),
                run.Status=="IN_PROGRESS"&&decisions.Any(d=>d.Action=="CONTINUE")&&!decisions.Any(d=>d.Action=="CANCEL"));
        },ct);

    public Task<ExecutionItem> DecideSecurityHoldAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid securityHold,DecideSecurityHold request,CancellationToken ct=default)=>
        ExecutionWrite(actor,organization,incident,request.Continue?"SECURITY_CONTINUITY_AUTHORIZED":"SECURITY_EXECUTION_CANCELLED",request.ExpectedIncidentRevision,request.Reason,async parent=>
        {
            if(!await db.OrganizationMemberships.AnyAsync(m=>m.OrganizationId==organization&&m.UserId==actor&&m.Status==MembershipStatus.Active&&m.Role==OrganizationRole.OrgAdmin,ct))throw Error(403,"organization_admin_required");
            var run=await ActiveExecution(organization,incident,execution,request.ExpectedExecutionRevision,ct);
            var hold=await EffectiveHold(run.VersionId,null,ct);
            if(hold is null||hold.Id!=securityHold)throw Error(409,"security_hold_obsolete");
            var action=request.Continue?"CONTINUE":"CANCEL";
            if(await db.ExecutionSecurityDecisions.AnyAsync(d=>d.ExecutionId==execution&&d.SecurityHoldId==securityHold&&d.Action==action,ct))throw Error(409,"security_hold_already_decided");
            db.ExecutionSecurityDecisions.Add(new(){OrganizationId=organization,IncidentId=incident,VersionId=run.VersionId,ExecutionId=execution,SecurityHoldId=securityHold,
                Action=action,ExecutionRevision=run.Revision,IncidentRevision=parent.Revision,ActorUserId=actor,CorrelationId=context.CorrelationId,Reason=request.Reason.Trim()});
            if(!request.Continue){run.Status="CANCELLED";run.FinishReason=request.Reason.Trim();var now=DateTimeOffset.UtcNow;run.FinishedAt=now.AddTicks(-(now.Ticks%10));}
            return(run,null);
        },ct);
    public Task<IReadOnlyList<SecurityDecisionItem>> SecurityDecisionsAsync(Guid actor,Guid organization,Guid incident,Guid execution,long afterRevision=0,int limit=50,CancellationToken ct=default)=>
        ExecutionRead<IReadOnlyList<SecurityDecisionItem>>(actor,organization,incident,async()=>
        {
            if(afterRevision<0||limit is <1 or >100)throw Error(400,"invalid_execution_pagination");
            if(!await db.PlaybookExecutions.AnyAsync(e=>e.Id==execution&&e.OrganizationId==organization&&e.IncidentId==incident,ct))throw Error(404,"execution_not_found");
            return await db.ExecutionSecurityDecisions.AsNoTracking().Where(d=>d.OrganizationId==organization&&d.IncidentId==incident&&d.ExecutionId==execution&&d.ExecutionRevision>afterRevision)
                .OrderBy(d=>d.ExecutionRevision).Take(limit).Select(d=>new SecurityDecisionItem(d.Id,d.SecurityHoldId,d.Action,d.ExecutionRevision,d.IncidentRevision,d.ActorUserId,d.CorrelationId,d.Reason,d.OccurredAt)).ToListAsync(ct);
        },ct);
}
