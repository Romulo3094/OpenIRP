using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Assistance;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Assistance;
namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private sealed record AiScope(Incident Parent,PlaybookExecution Run,ExecutionTask Task)
    { public bool Active => Parent.Status=="IN_RESPONSE" && Run.Status=="IN_PROGRESS" && Task.Status is "PENDING" or "BLOCKED" or "IN_PROGRESS"; }
    private async Task<AiScope> AuthorizeAssistance(Guid actor,Guid org,Guid inc,Guid runId,Guid taskId,bool active,CancellationToken ct)
    {
        await AuthorizeAsync(actor,org,true,ct);
        var parent=await db.Incidents.AsNoTracking().SingleOrDefaultAsync(i=>i.OrganizationId==org&&i.Id==inc,ct)??throw Error(404,"incident_not_found");
        var run=await db.PlaybookExecutions.AsNoTracking().SingleOrDefaultAsync(e=>e.OrganizationId==org&&e.IncidentId==inc&&e.Id==runId,ct)??throw Error(404,"execution_not_found");
        var task=await db.ExecutionTasks.AsNoTracking().SingleOrDefaultAsync(t=>t.OrganizationId==org&&t.IncidentId==inc&&t.ExecutionId==runId&&t.VersionId==run.VersionId&&t.Id==taskId,ct)??throw Error(404,"execution_task_not_found");
        var member=await db.OrganizationMemberships.AsNoTracking().SingleAsync(m=>m.OrganizationId==org&&m.UserId==actor,ct);
        if(member.Role!=OrganizationRole.OrgAdmin&&parent.AssignedMembershipId!=member.Id&&task.AssignedMembershipId!=member.Id)throw Error(403,"ai_assigned_writer_required");
        if(!await db.Playbooks.AnyAsync(p=>p.Id==run.PlaybookId&&(p.OrganizationId==null||p.OrganizationId==org),ct))throw Error(404,"execution_playbook_not_found");
        var scope=new AiScope(parent,run,task);
        if(active){if(!scope.Active)throw Error(409,"ai_active_task_required");await RequireUsableExecution(run,ct);}
        return scope;
    }
    private async Task<AssistanceContext> BuildAssistanceContext(Guid actor,Guid org,Guid inc,Guid run,Guid task,CancellationToken ct)
    {
        var scope=await AuthorizeAssistance(actor,org,inc,run,task,true,ct);
        var step=await db.PlaybookSteps.AsNoTracking().SingleAsync(s=>s.Id==scope.Task.StepId&&s.VersionId==scope.Run.VersionId,ct);
        var requirements=await(from r in db.PlaybookCapabilityRequirements.AsNoTracking().Where(r=>r.VersionId==scope.Run.VersionId&&(r.StepId==null||r.StepId==step.Id))
            join c in db.CapabilityCatalog.AsNoTracking() on r.CapabilityId equals c.Id
            join p in db.OrganizationCapabilities.AsNoTracking().Where(p=>p.OrganizationId==org) on c.Id equals p.CapabilityId into profiles
            from p in profiles.DefaultIfEmpty()
            join a in db.IncidentCapabilityAssessments.AsNoTracking().Where(a=>a.OrganizationId==org&&a.IncidentId==inc) on c.Id equals a.CapabilityId into overrides
            from a in overrides.DefaultIfEmpty()
            orderby c.Code,r.Level
            select new {Catalog=c,Profile=p,Assessment=a,r.Level}).Take(301).ToListAsync(ct);
        if(requirements.Count>300)throw Error(409,"ai_context_too_large");
        var capabilities=requirements.Select(r=>new AssistanceCapability(r.Catalog.Code,r.Level.ToString().ToUpperInvariant(),r.Catalog.IsActive,
            CapabilityView(r.Catalog,r.Profile,r.Assessment).EffectiveAvailability.ToString().ToUpperInvariant())).ToList();
        var dependencies=await(from d in db.PlaybookStepDependencies.AsNoTracking().Where(d=>d.VersionId==scope.Run.VersionId&&d.SuccessorId==step.Id)
            join s in db.PlaybookSteps.AsNoTracking() on d.PredecessorId equals s.Id
            join t in db.ExecutionTasks.AsNoTracking().Where(t=>t.OrganizationId==org&&t.IncidentId==inc&&t.ExecutionId==run&&t.VersionId==scope.Run.VersionId) on s.Id equals t.StepId
            orderby s.Code select new AssistanceDependency(s.Code,t.Status)).Take(101).ToListAsync(ct);
        if(dependencies.Count>100)throw Error(409,"ai_context_too_large");
        var category=await db.IncidentCategories.Where(c=>c.Id==scope.Parent.CategoryId).Select(c=>c.Code).SingleAsync(ct);var i=scope.Parent;
        var input=new AssistanceInput(OllamaLocalAssistant.SchemaVersion,category,i.Severity?.ToString(),i.MaximumSeverityReached?.ToString(),
            i.ConfidentialityImpact?.ToString(),i.IntegrityImpact?.ToString(),i.AvailabilityImpact?.ToString(),i.Scope?.ToString(),i.OperationalImpact?.ToString(),i.AssessedCriticality?.ToString(),
            step.Code,step.Phase,step.Instructions,scope.Task.Status,step.IsRequired,step.RequiresApproval,step.RequiresEvidence,capabilities,dependencies);
        if(!OllamaLocalAssistant.FitsInputBudget(input))throw Error(409,"ai_context_too_large");
        return new(task,i.Revision,scope.Run.Revision,await RiskContext(org,inc,ct),input);
    }
    public Task<AssistanceContext> AssistanceContextAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,CancellationToken ct=default)=>
        ExecutionRead(actor,organization,incident,()=>BuildAssistanceContext(actor,organization,incident,execution,task,ct),ct);
    private static AssistanceAssessmentItem AiView(AssistanceAssessment a,bool current)=>new(a.Id,a.TaskId,a.IncidentRevision,a.ExecutionRevision,a.ContextToken,a.InputHash,
        a.SchemaVersion,a.PromptVersion,a.Kind,a.Model,a.ModelDigest,a.DurationMs,a.Outcome,a.ErrorCode,
        a.RecommendationJson is null?null:JsonSerializer.Deserialize<StepExplanation>(a.RecommendationJson,OllamaLocalAssistant.Json),a.ActorUserId,a.CorrelationId,a.OccurredAt,current);
    public async Task<AssistanceAssessmentItem> ExplainStepAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,ExplainStep request,CancellationToken ct=default)
    {
        var attempt=new OpenIRP.Infrastructure.Audit.AuditAttempt("ai.assistance.request",actor,organization,"execution_task",task);
        try {
            Reason(request.Reason);if(request.Reason.Contains('\0'))throw Error(400,"invalid_ai_reason");
            var snapshot=await AssistanceContextAsync(actor,organization,incident,execution,task,ct);
            if(snapshot.IncidentRevision!=request.ExpectedIncidentRevision||snapshot.ExecutionRevision!=request.ExpectedExecutionRevision||snapshot.ContextToken!=request.ExpectedContext)throw Error(409,"stale_ai_context");
            var clock=Stopwatch.StartNew();LocalAssistantReply reply;
            try{reply=await assistant.ExplainAsync(snapshot.Input,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch{reply=new("unconfigured",null,"UNAVAILABLE","ai_unavailable",null);}
            clock.Stop();
            if(reply.Outcome=="VALID"&&!OllamaLocalAssistant.ValidExplanation(reply.Recommendation,snapshot.Input.StepCode))reply=reply with{Outcome="INVALID",ErrorCode="ai_invalid_output",Recommendation=null};
            if(reply.Outcome is not ("VALID" or "INVALID" or "UNAVAILABLE"))reply=new("unconfigured",null,"UNAVAILABLE","ai_invalid_provider_reply",null);
            var errors=new[]{"ai_disabled","ai_invalid_configuration","ai_busy","ai_unavailable","ai_invalid_transport","ai_local_model_missing","ai_invalid_output","ai_timeout","ai_invalid_provider_reply","ai_context_too_large","ai_reasoning_output_rejected"};
            if(reply.Model is not ("qwen3:4b-instruct" or "unconfigured"))reply=reply with{Model="unconfigured"};
            if(reply.ModelDigest?.Length!=64||!reply.ModelDigest.All(Uri.IsHexDigit))reply=reply with{ModelDigest=null};
            else reply=reply with{ModelDigest=reply.ModelDigest.ToLowerInvariant()};
            if(reply.Outcome=="VALID"&&(reply.ModelDigest is null||reply.Model=="unconfigured"))reply=reply with{Outcome="INVALID",ErrorCode="ai_invalid_provider_reply",Recommendation=null};
            reply=reply with{ErrorCode=reply.Outcome=="VALID"?null:errors.Contains(reply.ErrorCode)?reply.ErrorCode:"ai_invalid_provider_reply"};
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'",ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978,26)",ct);
            var now=await AuthorizeAssistance(actor,organization,incident,execution,task,false,ct);
            var token=await RiskContext(organization,incident,ct);
            var current=now.Active&&now.Parent.Revision==snapshot.IncidentRevision&&now.Run.Revision==snapshot.ExecutionRevision&&token==snapshot.ContextToken;
            if(current)current=await db.Database.SqlQuery<bool>($"""SELECT app.execution_version_usable({execution}) AS "Value" """).SingleAsync(ct);
            if(!current)reply=reply with{Outcome="STALE",ErrorCode="ai_context_changed",Recommendation=null};
            var input=JsonSerializer.Serialize(snapshot.Input,OllamaLocalAssistant.Json);
            var normalized=reply.Outcome=="VALID"?reply.Recommendation! with{Guidance=reply.Recommendation!.Guidance.Trim(),Cautions=reply.Recommendation.Cautions.Select(x=>x.Trim()).ToArray(),Assumptions=reply.Recommendation.Assumptions.Select(x=>x.Trim()).ToArray()}:null;
            var row=new AssistanceAssessment{OrganizationId=organization,IncidentId=incident,ExecutionId=execution,TaskId=task,VersionId=now.Run.VersionId,
                IncidentRevision=snapshot.IncidentRevision,ExecutionRevision=snapshot.ExecutionRevision,ContextToken=snapshot.ContextToken,ContextJson=input,
                InputHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input))),SchemaVersion=OllamaLocalAssistant.SchemaVersion,
                PromptVersion=OllamaLocalAssistant.PromptVersion,ParametersJson="{\"temperature\":0,\"num_predict\":384,\"num_ctx\":4096,\"stream\":false,\"think\":false}",
                Reason=request.Reason.Trim(),Model=reply.Model,ModelDigest=reply.ModelDigest,DurationMs=Math.Min(clock.ElapsedMilliseconds,600000),Outcome=reply.Outcome,
                ErrorCode=reply.ErrorCode,RecommendationJson=normalized is null?null:JsonSerializer.Serialize(normalized,OllamaLocalAssistant.Json),
                ActorUserId=actor,CorrelationId=context.CorrelationId,OccurredAt=EvidenceNow()};
            db.AssistanceAssessments.Add(row);
            db.AuditEvents.Add(audit.Success(new("ai.assistance.evaluated",actor,organization,"assistance_assessment",row.Id),JsonSerializer.Serialize(new{row.Kind,row.Outcome,row.ErrorCode,row.Model,row.ModelDigest,row.DurationMs},OllamaLocalAssistant.Json)));
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return AiView(row,current&&row.Outcome=="VALID");
        }catch(Exception ex){await audit.RejectedAsync(attempt,ex);throw;}
        finally{db.ChangeTracker.Clear();}
    }
    public Task<AssistanceAssessmentPage> AssistanceHistoryAsync(Guid actor,Guid organization,Guid incident,Guid execution,Guid task,int skip=0,int limit=50,CancellationToken ct=default)=>
        ExecutionRead(actor,organization,incident,async()=>{
            if(skip is <0 or >1000000||limit is <1 or >100)throw Error(400,"invalid_ai_pagination");
            var scope=await AuthorizeAssistance(actor,organization,incident,execution,task,false,ct);var token=await RiskContext(organization,incident,ct);
            var rows=await db.AssistanceAssessments.AsNoTracking().Where(a=>a.OrganizationId==organization&&a.IncidentId==incident&&a.ExecutionId==execution&&a.TaskId==task)
                .OrderBy(a=>a.OccurredAt).ThenBy(a=>a.Id).Skip(skip).Take(limit+1).ToListAsync(ct);
            return new AssistanceAssessmentPage(rows.Take(limit).Select(a=>AiView(a,scope.Active&&a.Outcome=="VALID"&&a.ContextToken==token
                &&a.IncidentRevision==scope.Parent.Revision&&a.ExecutionRevision==scope.Run.Revision&&a.PromptVersion==OllamaLocalAssistant.PromptVersion)).ToList(),rows.Count>limit?skip+limit:null);
        },ct);
}
