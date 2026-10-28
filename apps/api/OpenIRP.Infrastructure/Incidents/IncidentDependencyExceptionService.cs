using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task RequireDependencyExceptionTask(PlaybookExecution run, ExecutionTask task, CancellationToken ct)
    {
        await RequireUsableExecution(run, ct);
        if (task.Status is not ("PENDING" or "BLOCKED")) throw Error(409, "dependency_exception_requires_unstarted_or_blocked_task");
        if (task.AssignedMembershipId is not Guid assigned) throw Error(409, "execution_task_unassigned");
        await EligibleMembership(task.OrganizationId, assigned, null, ct);
    }

    private Task<string> DependencyExceptionContext(Guid execution, Guid task, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"""SELECT app.dependency_exception_context({execution}, {task})::text AS "Value" """).SingleAsync(ct);

    private Task<bool> HasDependencyException(Guid task, Guid predecessor, Guid assigned, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""SELECT app.has_dependency_exception({task}, {predecessor}, {assigned}) AS "Value" """).SingleAsync(ct);

    private async Task RequireExecutionDependencies(PlaybookExecution execution, ExecutionTask task, CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""SELECT app.execution_dependencies_satisfied({execution.Id}, {task.Id}, {task.AssignedMembershipId}) AS "Value" """).SingleAsync(ct))
            throw Error(409, "execution_dependencies_not_completed");
    }

    private async Task<(ExecutionTask Predecessor, Guid Dependency, Guid Skip)> DependencyEdge(PlaybookExecution run, ExecutionTask task, Guid predecessor, bool requireSkipped, CancellationToken ct)
    {
        var previous = await FindTask(run, predecessor, ct);
        var edge = await db.PlaybookStepDependencies.AsNoTracking().SingleOrDefaultAsync(d => d.VersionId == run.VersionId &&
            d.PredecessorId == previous.StepId && d.SuccessorId == task.StepId, ct) ?? throw Error(404, "execution_dependency_not_found");
        if (!requireSkipped) return (previous, edge.Id, Guid.Empty);
        if (previous.Status != "SKIPPED") throw Error(409, "dependency_exception_requires_skipped_predecessor");
        var skip = await db.SkipAuthorizations.AsNoTracking().Where(a => a.TaskId == previous.Id && a.ExecutionId == run.Id && a.Action == "APPROVED")
            .Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct) ?? throw Error(409, "predecessor_skip_authorization_missing");
        return (previous, edge.Id, skip);
    }

    private void AddDependencyException(Guid actor, Incident incident, PlaybookExecution run, ExecutionTask task, Guid predecessor, Guid dependency, Guid skip,
        string action, string reason, string impact, string risk, string snapshot, Guid? request = null, bool self = false, string? justification = null) =>
        db.DependencyExceptions.Add(new()
        {
            OrganizationId=run.OrganizationId, IncidentId=run.IncidentId, VersionId=run.VersionId, ExecutionId=run.Id, TaskId=task.Id,
            PredecessorTaskId=predecessor, DependencyId=dependency, SkipAuthorizationId=skip, AssignedMembershipId=task.AssignedMembershipId!.Value,
            Action=action, ExecutionRevision=run.Revision, IncidentRevision=incident.Revision, ActorUserId=actor, CorrelationId=context.CorrelationId,
            Reason=reason.Trim(), Impact=impact.Trim(), ResidualRisk=risk.Trim(), DependencyContextJson=snapshot,
            RequestId=request, SelfApproval=self, SelfApprovalJustification=justification?.Trim()
        });

    public Task<ExecutionItem> RequestDependencyExceptionAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor,
        RequestDependencyException request, CancellationToken ct = default) =>
        ExecutionWrite(actor, organization, incident, "DEPENDENCY_EXCEPTION_REQUESTED", request.ExpectedIncidentRevision, request.Reason, async entity =>
        {
            Reason(request.Impact); Reason(request.ResidualRisk);
            var run=await ActiveExecution(organization,incident,execution,request.ExpectedExecutionRevision,ct);
            var item=await FindTask(run,task,ct); await RequireDependencyExceptionTask(run,item,ct);
            await EligibleMembership(organization,item.AssignedMembershipId!.Value,actor,ct);
            var edge=await DependencyEdge(run,item,predecessor,true,ct);
            var snapshot=await DependencyExceptionContext(execution,task,ct);
            if (await HasDependencyException(task,predecessor,item.AssignedMembershipId.Value,ct))
                throw Error(409,"dependency_exception_already_approved");
            var previous=await db.DependencyExceptions.AsNoTracking().Where(a=>a.TaskId==task && a.PredecessorTaskId==predecessor && a.Action=="REQUESTED")
                .OrderByDescending(a=>a.ExecutionRevision).FirstOrDefaultAsync(ct);
            if (previous is not null && previous.ExecutionRevision==run.Revision-1 && previous.IncidentRevision==entity.Revision-1 &&
                previous.DependencyContextJson==snapshot && !await db.DependencyExceptions.AnyAsync(a=>a.RequestId==previous.Id && a.Action=="REJECTED",ct))
                throw Error(409,"dependency_exception_already_pending");
            AddDependencyException(actor,entity,run,item,predecessor,edge.Dependency,edge.Skip,"REQUESTED",request.Reason,request.Impact,request.ResidualRisk,snapshot);
            return (run,item);
        },ct);

    public Task<ExecutionItem> DecideDependencyExceptionAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor,
        Guid exceptionRequest, DecideDependencyException request, CancellationToken ct = default) =>
        ExecutionWrite(actor,organization,incident,request.Approve?"DEPENDENCY_EXCEPTION_APPROVED":"DEPENDENCY_EXCEPTION_REJECTED",
            request.ExpectedIncidentRevision,request.Reason,async entity=>
        {
            if (!await db.OrganizationMemberships.AnyAsync(m=>m.OrganizationId==organization && m.UserId==actor && m.Status==MembershipStatus.Active && m.Role==OrganizationRole.OrgAdmin,ct))
                throw Error(403,"organization_admin_required");
            var run=await ActiveExecution(organization,incident,execution,request.ExpectedExecutionRevision,ct);
            var item=await FindTask(run,task,ct);await RequireDependencyExceptionTask(run,item,ct);
            var edge=await DependencyEdge(run,item,predecessor,true,ct);
            var pending=await db.DependencyExceptions.AsNoTracking().Where(a=>a.TaskId==task && a.PredecessorTaskId==predecessor && a.Action=="REQUESTED")
                .OrderByDescending(a=>a.ExecutionRevision).FirstOrDefaultAsync(ct);
            if(pending is null || pending.Id!=exceptionRequest)throw Error(409,"dependency_exception_request_obsolete");
            if(await db.DependencyExceptions.AnyAsync(a=>a.RequestId==pending.Id,ct))throw Error(409,"dependency_exception_already_decided");
            if(pending.ExecutionRevision!=run.Revision-1 || pending.IncidentRevision!=entity.Revision-1 ||
                pending.AssignedMembershipId!=item.AssignedMembershipId || pending.DependencyContextJson!=await DependencyExceptionContext(execution,task,ct))
                throw Error(409,"dependency_exception_context_changed");
            var self=request.Approve && actor==pending.ActorUserId;
            if(self!=request.SelfApproval || (self && (string.IsNullOrWhiteSpace(request.SelfApprovalJustification) || request.SelfApprovalJustification.Length>2000))
                || (!self && request.SelfApprovalJustification is not null))throw Error(400,"invalid_dependency_self_approval");
            AddDependencyException(actor,entity,run,item,predecessor,edge.Dependency,edge.Skip,request.Approve?"APPROVED":"REJECTED",request.Reason,
                pending.Impact,pending.ResidualRisk,pending.DependencyContextJson,pending.Id,self,request.SelfApprovalJustification);
            return (run,item);
        },ct);

    public Task<IReadOnlyList<DependencyExceptionItem>> DependencyExceptionsAsync(Guid actor, Guid organization, Guid incident, Guid execution, Guid task, Guid predecessor,
        long afterRevision=0,int limit=50,CancellationToken ct=default) =>
        ExecutionRead<IReadOnlyList<DependencyExceptionItem>>(actor,organization,incident,async ()=>
        {
            if(afterRevision<0 || limit is <1 or >100)throw Error(400,"invalid_execution_pagination");
            var run=await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==execution && e.OrganizationId==organization && e.IncidentId==incident,ct)
                ?? throw Error(404,"execution_not_found");
            var item=await FindTask(run,task,ct);await DependencyEdge(run,item,predecessor,false,ct);
            var rows=await db.DependencyExceptions.AsNoTracking().Where(a=>a.OrganizationId==organization && a.IncidentId==incident &&
                a.ExecutionId==execution && a.TaskId==task && a.PredecessorTaskId==predecessor && a.ExecutionRevision>afterRevision)
                .OrderBy(a=>a.ExecutionRevision).Take(limit).ToListAsync(ct);
            return rows.Select(a=>new DependencyExceptionItem(a.Id,a.RequestId,a.Action,a.DependencyId,a.PredecessorTaskId,a.SkipAuthorizationId,a.AssignedMembershipId,
                a.ExecutionRevision,a.IncidentRevision,a.ActorUserId,a.CorrelationId,a.Reason,a.Impact,a.ResidualRisk,
                JsonSerializer.Deserialize<JsonElement>(a.DependencyContextJson),a.SelfApproval,a.SelfApprovalJustification,a.OccurredAt)).ToList();
        },ct);
}
