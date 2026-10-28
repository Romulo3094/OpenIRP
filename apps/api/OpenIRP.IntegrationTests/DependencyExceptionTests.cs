using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<(ExecutionFixture Fixture,ExecutionItem Run)> DependencySeed(bool controls=false)
    {
        var f=await ExecutionSeed(customize:controls?c=>c with{Steps=c.Steps.Select((s,i)=>s with{RequiresApproval=i==1,RequiresEvidence=i==1}).ToList()}:null);
        var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);e=await AskSkip(f,e);e=await DecideSkip(f,e,await LastSkipRequest(f,e));
        e=await AssignTask(f,e,e.Tasks[1].Id);return(f,e);
    }
    private Task<ExecutionItem> AskDependency(ExecutionFixture f,ExecutionItem e,Guid? actor=null,Guid? target=null,Guid? predecessor=null,
        string impact="Impacto de continuar sin la acción previa",string risk="Riesgo residual para revisión administrativa") =>
        Executions(s=>s.RequestDependencyExceptionAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,target??e.Tasks[1].Id,predecessor??e.Tasks[0].Id,
            new(impact,risk,e.Revision,e.IncidentRevision,"Solicitar excepción de vínculo")));
    private async Task<Guid> LastDependencyRequest(ExecutionFixture f,ExecutionItem e,Guid? target=null,Guid? predecessor=null) =>
        (await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target??e.Tasks[1].Id,predecessor??e.Tasks[0].Id))).Last(a=>a.Action=="REQUESTED").Id;
    private Task<ExecutionItem> DecideDependency(ExecutionFixture f,ExecutionItem e,Guid request,bool approve=true,Guid? actor=null,Guid? target=null,Guid? predecessor=null,
        bool self=true,string? justification="Excepción explícita del administrador único") =>
        Executions(s=>s.DecideDependencyExceptionAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,target??e.Tasks[1].Id,predecessor??e.Tasks[0].Id,request,
            new(approve,self,justification,e.Revision,e.IncidentRevision,"Decisión revisada del vínculo")));

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyExceptionAllowsOnlyTheApprovedEdgeAndPreservesSkippedPredecessorAndHistory()
    {
        var(f,e)=await DependencySeed();var target=e.Tasks[1].Id;var pred=e.Tasks[0].Id;
        var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,target,member.Id);
        await Rejected(409,()=>TaskState(f,e,target,ExecutionTaskState.InProgress,f.Platform),"execution_dependencies_not_completed");
        e=await AskDependency(f,e,f.Platform);var request=await LastDependencyRequest(f,e);
        await Rejected(403,()=>DecideDependency(f,e,request,actor:f.Platform));
        e=await DecideDependency(f,e,request,self:false,justification:null);
        Assert.Equal("PENDING",e.Tasks[1].Status);
        e=await TaskState(f,e,target,ExecutionTaskState.InProgress,f.Platform);
        e=await TaskState(f,e,target,ExecutionTaskState.Completed,f.Platform);
        Assert.Equal("SKIPPED",e.Tasks[0].Status);
        var history=await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred));
        Assert.Equal(new[]{"REQUESTED","APPROVED"},history.Select(a=>a.Action));Assert.Equal(history[0].Id,history[1].RequestId);
        Assert.Equal(history[0].SkipAuthorizationId,history[1].SkipAuthorizationId);
        Assert.Equal("dependency-exception-context-v1",history[0].DependencyContext.GetProperty("format").GetString());
        Assert.NotEmpty(history[0].DependencyContext.GetProperty("capabilities").EnumerateArray());
        Assert.Equal("Unknown",history[0].DependencyContext.GetProperty("capabilities")[0].GetProperty("effectiveAvailability").GetString());
        Assert.Equal("Undeclared",history[0].DependencyContext.GetProperty("capabilities")[0].GetProperty("source").GetString());
        var events=await Executions(s=>s.EventsAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        foreach(var row in history){var ev=Assert.Single(events,a=>a.Revision==row.ExecutionRevision);Assert.Equal(row.CorrelationId,ev.CorrelationId);Assert.Equal(row.IncidentRevision,ev.IncidentRevision);}
        Assert.Equal(2,await InScope((_,db)=>db.PlaybookStepDependencies.CountAsync()));
        Assert.Equal("IN_RESPONSE",(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Status);
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData(false,null)] [InlineData(true,null)] [InlineData(true," ")]
    public async Task DependencySelfApprovalRequiresAnExplicitJustifiedException(bool self,string? reason)
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);var id=await LastDependencyRequest(f,e);
        await Rejected(400,()=>DecideDependency(f,e,id,self:self,justification:reason),"invalid_dependency_self_approval");
        e=await DecideDependency(f,e,id);e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        Assert.Equal("IN_PROGRESS",e.Tasks[1].Status);
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("impact")] [InlineData("risk")] [InlineData("long-impact")] [InlineData("long-risk")]
    public async Task DependencyExceptionRequiresBoundedImpactAndRisk(string field)
    {
        var(f,e)=await DependencySeed();
        await Rejected(400,()=>AskDependency(f,e,impact:field=="impact"?" ":field=="long-impact"?new string('x',2001):"Impacto",
            risk:field=="risk"?" ":field=="long-risk"?new string('x',2001):"Riesgo"));
        Assert.Equal(0,await InScope((_,db)=>db.DependencyExceptions.CountAsync()));
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("pending")] [InlineData("in-progress")] [InlineData("completed")]
    public async Task ExceptionCannotBypassAnUnskippedPredecessor(string status)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        if(status!="pending"){e=await AssignTask(f,e,e.Tasks[0].Id);e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);}
        if(status=="completed")e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.Completed);
        e=await AssignTask(f,e,e.Tasks[1].Id);
        await Rejected(409,()=>AskDependency(f,e),"dependency_exception_requires_skipped_predecessor");
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("assignment")] [InlineData("other-task")] [InlineData("incident")] [InlineData("profile")] [InlineData("catalog")] [InlineData("assessment")]
    public async Task PendingDependencyDecisionRequiresFreshRevisionsAndContext(string change)
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);var id=await LastDependencyRequest(f,e);
        var capability=f.Playbook.Requirements[0].CapabilityId;
        if(change=="assignment")e=await AssignTask(f,e,e.Tasks[1].Id);
        else if(change=="other-task")e=await AssignTask(f,e,e.Tasks[2].Id);
        else if(change=="profile")await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Nuevo perfil",0)));
        else if(change=="catalog")await Inventory(s=>s.UpdateCatalogAsync(f.Platform,CatalogKind.Capabilities,capability,new("Nombre revisado","CUSTOM",null,true,1)));
        else if(change=="assessment")await Incidents(s=>s.AssessCapabilityAsync(f.Admin,f.Org,f.Incident.Id,capability,Assessment(e.IncidentRevision,CapabilityAvailability.Unknown)));
        else await Incidents(s=>s.AssignAsync(f.Admin,f.Org,f.Incident.Id,new(f.Membership,"Revisión del contexto",e.IncidentRevision)));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        await Rejected(409,()=>DecideDependency(f,e,id),"dependency_exception_context_changed");
        e=await AskDependency(f,e);var fresh=await LastDependencyRequest(f,e);Assert.NotEqual(id,fresh);
        e=await DecideDependency(f,e,fresh);e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("profile")] [InlineData("catalog")] [InlineData("assessment")] [InlineData("incident")] [InlineData("assignment")]
    public async Task ApprovedDependencyExceptionCannotBeReusedAfterRiskContextOrAssignmentChanges(string change)
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        var capability=f.Playbook.Requirements[0].CapabilityId;
        if(change=="assignment")e=await AssignTask(f,e,e.Tasks[1].Id);
        else if(change=="profile")await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Perfil nuevo",0)));
        else if(change=="catalog")await Inventory(s=>s.UpdateCatalogAsync(f.Platform,CatalogKind.Capabilities,capability,new("Referencia revisada","CUSTOM",null,true,1)));
        else if(change=="assessment")await Incidents(s=>s.AssessCapabilityAsync(f.Admin,f.Org,f.Incident.Id,capability,Assessment(e.IncidentRevision,CapabilityAvailability.Unknown)));
        else await Incidents(s=>s.AssignAsync(f.Admin,f.Org,f.Incident.Id,new(f.Membership,"Cambió el contexto",e.IncidentRevision)));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
        e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyExceptionSurvivesProgressButInProgressReassignmentRequiresBlocking()
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);var id=await LastDependencyRequest(f,e);e=await DecideDependency(f,e,id);
        await Rejected(409,()=>AskDependency(f,e),"dependency_exception_already_approved");
        e=await AssignTask(f,e,e.Tasks[2].Id);
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        await Rejected(409,()=>AssignTask(f,e,e.Tasks[1].Id),"block_task_before_reassignment");
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.Blocked);
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress);
        e=await TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.Completed);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task RejectionAllowsNewRequestAndDecisionsCannotBeRewritten()
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);var old=await LastDependencyRequest(f,e);
        await Rejected(409,()=>AskDependency(f,e),"dependency_exception_already_pending");
        e=await DecideDependency(f,e,old,approve:false,self:false,justification:null);
        await Rejected(409,()=>DecideDependency(f,e,old),"dependency_exception_already_decided");
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress));
        e=await AskDependency(f,e);var fresh=await LastDependencyRequest(f,e);
        await Rejected(409,()=>DecideDependency(f,e,old),"dependency_exception_request_obsolete");
        e=await DecideDependency(f,e,fresh);Assert.Equal("PENDING",e.Tasks[1].Status);
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("organization")] [InlineData("assignee")] [InlineData("account")] [InlineData("lockout")] [InlineData("withdrawn")] [InlineData("cancelled")]
    public async Task DependencyDecisionRechecksParticipantsAndUsableExecution(string invalid)
    {
        var(f,e)=await DependencySeed();var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,e.Tasks[1].Id,member.Id);e=await AskDependency(f,e,f.Platform);var id=await LastDependencyRequest(f,e);
        if(invalid=="organization")await InScope(async(_,db)=>{(await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;return await db.SaveChangesAsync();});
        else if(invalid=="assignee")await Act(s=>s.ChangeMemberAsync(f.Admin,f.Org,member.Id,new(OrganizationRole.Analyst,MembershipStatus.Suspended,member.UpdatedAt)));
        else if(invalid is "account" or "lockout")await InScope(async(_,db)=>{var u=await db.Users.SingleAsync(u=>u.Id==f.Platform);if(invalid=="account")u.IsActive=false;else u.LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);return await db.SaveChangesAsync();});
        else if(invalid=="withdrawn")await Playbooks(s=>s.WithdrawAsync(f.Platform,null,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Retirada",3)));
        else e=await Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Cancelación")));
        await Rejected(invalid=="organization"?403:409,()=>DecideDependency(f,e,id,self:false,justification:null));
        Assert.Equal(1,await InScope((_,db)=>db.DependencyExceptions.CountAsync()));
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("request","audit")] [InlineData("decision","audit")] [InlineData("request","exceptions")] [InlineData("decision","exceptions")] [InlineData("decision","events")]
    public async Task DependencyFailureRollsBackBothRevisionsAndDecision(string operation,string sink)
    {
        var(f,e)=await DependencySeed();Guid id=Guid.Empty;if(operation=="decision"){e=await AskDependency(f,e);id=await LastDependencyRequest(f,e);}
        var count=await InScope((_,db)=>db.DependencyExceptions.CountAsync());
        var table=sink=="audit"?"audit.audit_events":sink=="exceptions"?"app.dependency_exceptions":"app.execution_task_events";
        var sql="CREATE FUNCTION app.fail_dependency_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected failure'; END; $body$; CREATE TRIGGER fail_dependency_test BEFORE INSERT ON "+table+" FOR EACH ROW "+(sink=="audit"?"WHEN (NEW.outcome='SUCCESS') ":"")+"EXECUTE FUNCTION app.fail_dependency_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql));
        await Assert.ThrowsAsync<DbUpdateException>(async()=>{if(operation=="request")await AskDependency(f,e);else await DecideDependency(f,e,id);});
        var after=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal(e.Revision,after.Revision);Assert.Equal(e.IncidentRevision,after.IncidentRevision);
        Assert.Equal(count,await InScope((_,db)=>db.DependencyExceptions.CountAsync()));
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress));
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData("update")] [InlineData("delete")] [InlineData("truncate")]
    public async Task DependencyHistoryIsAppendOnlyInSqlAndEf(string operation)
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);
        var sql=operation=="update"?"UPDATE app.dependency_exceptions SET impact='Alterado'":operation=="delete"?"DELETE FROM app.dependency_exceptions":"TRUNCATE app.dependency_exceptions,app.dependency_exception_revocations";
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));Assert.Equal("42501",error.SqlState);
        await InScope(async(_,db)=>{var a=await db.DependencyExceptions.SingleAsync();a.Impact="Alterado";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
    }

    [Theory,Trait("Feature","DependencyExceptions")]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentDependencyRequestsAndDecisionsHaveOneWinner(bool decision)
    {
        var(f,e)=await DependencySeed();Guid id=Guid.Empty;if(decision){e=await AskDependency(f,e);id=await LastDependencyRequest(f,e);}
        async Task<bool> Attempt(){try{if(decision)await DecideDependency(f,e,id);else await AskDependency(f,e);return true;}catch(IdentityOperationException ex){Assert.Equal(409,ex.StatusCode);return false;}}
        Assert.Single(await Task.WhenAll(Attempt(),Attempt()),x=>x);
        Assert.Equal(decision?2:1,await InScope((_,db)=>db.DependencyExceptions.CountAsync()));
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyScopeRolesPaginationAndStaleTokensAreEnforced()
    {
        var(f,e)=await DependencySeed();var target=e.Tasks[1].Id;var pred=e.Tasks[0].Id;
        await Rejected(403,()=>Executions(s=>s.DependencyExceptionsAsync(f.Platform,f.Org,f.Incident.Id,e.Id,target,pred)));
        await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Viewer)));
        Assert.Empty(await Executions(s=>s.DependencyExceptionsAsync(f.Platform,f.Org,f.Incident.Id,e.Id,target,pred)));
        await Rejected(403,()=>AskDependency(f,e,f.Platform));
        await Rejected(404,()=>AskDependency(f,e,predecessor:e.Tasks[2].Id));
        await Rejected(404,()=>AskDependency(f,e,predecessor:Guid.NewGuid()));
        await Rejected(404,()=>Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,Guid.NewGuid(),e.Id,target,pred)));
        await Rejected(400,()=>Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred,-1)));
        await Rejected(400,()=>Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred,0,101)));
        var old=e;e=await AskDependency(f,e);
        await Rejected(409,()=>AskDependency(f,old),"stale_incident_revision");
        await Rejected(409,()=>Executions(s=>s.RequestDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred,new("Impacto","Riesgo",old.Revision,e.IncidentRevision,"Obsoleto"))),"stale_execution_revision");
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task HttpDependencyExceptionsRequireAuthenticationCsrfAndExplicitFlags()
    {
        var(f,e)=await DependencySeed();
        var route=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[1].Id}/dependencies/{e.Tasks[0].Id}/exceptions";
        using var anon=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(route)).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route,new RequestDependencyException("Impacto","Riesgo",e.Revision,e.IncidentRevision,"Petición HTTP"))).StatusCode);
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));var id=await LastDependencyRequest(f,e);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route+$"/{id}/decision",new{expectedExecutionRevision=e.Revision,expectedIncidentRevision=e.IncidentRevision,reason="Faltan flags"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route+$"/{id}/decision",new DecideDependencyException(true,true,"Excepción HTTP",e.Revision,e.IncidentRevision,"Autorizar"))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new RequestDependencyException("Impacto","Riesgo",e.Revision,e.IncidentRevision,"Sin CSRF"))).StatusCode);
    }
    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyExceptionDoesNotReplaceStepApprovalOrEvidenceAndSurvivesEvidenceRegistration()
    {
        var(f,e)=await DependencySeed(controls:true);var task=e.Tasks[1].Id;
        e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.InProgress),"task_approval_required");
        e=await Executions(s=>s.RequestApprovalAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new(e.Revision,e.IncidentRevision,"Autorizar paso controlado")));
        var approval=(await Executions(s=>s.ApprovalsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task))).Single(a=>a.Action=="REQUESTED").Id;
        e=await Executions(s=>s.DecideApprovalAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,approval,new(true,true,"Excepción explícita de paso",e.Revision,e.IncidentRevision,"Autorizar paso")));
        e=await TaskState(f,e,task,ExecutionTaskState.InProgress);
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.Completed),"task_evidence_required");
        var evidence=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Confidential,"Soporte","Resultado documentado",e.Id,task,e.IncidentRevision,"Registrar soporte")));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        e=await Executions(s=>s.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Completar",evidence.Evidence.Id)));
        Assert.Equal("COMPLETED",e.Tasks[1].Status);Assert.Equal("SKIPPED",e.Tasks[0].Status);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task EachMissingDependencyNeedsItsOwnDecisionWithoutRewritingThePublishedGraph()
    {
        var f=await ExecutionSeed(customize:c=>c with{Dependencies=[new("DETECT","RECOVER"),new("CONTAIN","RECOVER")]});
        var e=await BeginExecution(f);
        foreach(var task in e.Tasks.Take(2).ToArray()){e=await AssignTask(f,e,task.Id);e=await AskSkip(f,e,task:task.Id);e=await DecideSkip(f,e,await LastSkipRequest(f,e,task.Id),task:task.Id);}
        var target=e.Tasks[2].Id;e=await AssignTask(f,e,target);
        var first=e.Tasks[0].Id;var second=e.Tasks[1].Id;
        e=await AskDependency(f,e,target:target,predecessor:first);e=await DecideDependency(f,e,await LastDependencyRequest(f,e,target,first),target:target,predecessor:first);
        await Rejected(409,()=>TaskState(f,e,target,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
        e=await AskDependency(f,e,target:target,predecessor:second);e=await DecideDependency(f,e,await LastDependencyRequest(f,e,target,second),target:target,predecessor:second);
        e=await TaskState(f,e,target,ExecutionTaskState.InProgress);e=await TaskState(f,e,target,ExecutionTaskState.Completed);
        e=await Executions(s=>s.CompleteAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Finalizada con excepciones individuales")));
        Assert.Equal("COMPLETED",e.Status);Assert.All(e.Tasks.Take(2),t=>Assert.Equal("SKIPPED",t.Status));
        Assert.Equal(2,await InScope((_,db)=>db.PlaybookStepDependencies.CountAsync()));
        Assert.Equal("IN_RESPONSE",(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Status);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task ExpiredExceptionWhileInProgressRequiresBlockingAndFreshReviewBeforeCompletion()
    {
        var(f,e)=await DependencySeed();var task=e.Tasks[1].Id;e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        e=await TaskState(f,e,task,ExecutionTaskState.InProgress);
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,f.Playbook.Requirements[0].CapabilityId,new(CapabilityAvailability.Available,"Perfil revisado",0)));
        await Rejected(409,()=>TaskState(f,e,task,ExecutionTaskState.Completed),"execution_dependencies_not_completed");
        e=await TaskState(f,e,task,ExecutionTaskState.Blocked);e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        e=await TaskState(f,e,task,ExecutionTaskState.InProgress);e=await TaskState(f,e,task,ExecutionTaskState.Completed);
        Assert.Equal("COMPLETED",e.Tasks[1].Status);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task ContentFingerprintDetectsProfileChangesEvenWithoutARevisionIncrement()
    {
        var(f,e)=await DependencySeed();var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Contexto inicial",0)));
        e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        await InScope((_,db)=>db.Database.ExecuteSqlAsync($"UPDATE app.organization_capabilities SET notes='Cambio directo' WHERE organization_id={f.Org} AND capability_id={capability}"));
        Assert.Equal(1,await InScope((_,db)=>db.OrganizationCapabilities.Select(c=>c.Revision).SingleAsync()));
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DeferredDependencyGuardRejectsAnAssignmentMarkerInsertedAfterTheTaskUpdate()
    {
        var(f,e)=await DependencySeed();var task=e.Tasks[1].Id;e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        var exception=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE app.playbook_executions SET revision=revision+1 WHERE id={e.Id}");
            await db.Database.ExecuteSqlAsync($"UPDATE app.execution_tasks SET status='IN_PROGRESS',changed_at_revision={e.Revision+1} WHERE id={task}");
            db.ExecutionTaskEvents.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,VersionId=e.VersionId,ExecutionId=e.Id,TaskId=task,
                Revision=e.Revision+100,IncidentRevision=e.IncidentRevision,ExecutionStatus="IN_PROGRESS",TaskStatus="IN_PROGRESS",
                AssignedMembershipId=f.Membership,ActorUserId=f.Admin,CorrelationId=Guid.NewGuid(),Action="EXECUTION_TASK_ASSIGNED",Reason="Marca tardía inyectada"});
            await db.SaveChangesAsync();await tx.CommitAsync();return true;
        }));
        Assert.Equal("23514",exception.SqlState);Assert.Contains("Final task state",exception.MessageText);
        var current=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(e.Revision,current.Revision);Assert.Equal("PENDING",current.Tasks[1].Status);
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyMigrationUpgradesExistingExecutionAndPreventsHistoryLoss()
    {
        var(f,e)=await DependencySeed();
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026203604_SkipAuthorizations");await db.Database.MigrateAsync();return true;});
        e=await AskDependency(f,e);
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026203604_SkipAuthorizations");return true;}));
        await InScope(async(_,db)=>{await db.Database.MigrateAsync();return true;});
        Assert.Single(await Executions(s=>s.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[1].Id,e.Tasks[0].Id)));
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task RestrictedRuntimeCanAuthorizeAnEdgeWithoutRewritingDecisions()
    {
        var(f,e)=await DependencySeed();var target=e.Tasks[1].Id;var pred=e.Tasks[0].Id;var role="openirp_dependency_runtime_"+Guid.NewGuid().ToString("N");
        var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try
        {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grants=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grants.ExecuteNonQueryAsync();
            builder.Username=role;builder.Password=password;
            await using(var runtime=new TestApplication(builder.ConnectionString))
            {
                using var scope=runtime.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();
                e=await service.RequestDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred,new("Impacto runtime","Riesgo runtime",e.Revision,e.IncidentRevision,"Solicitar"));
                var request=(await service.DependencyExceptionsAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred)).Single().Id;
                e=await service.DecideDependencyExceptionAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,pred,request,new(true,true,"Excepción runtime",e.Revision,e.IncidentRevision,"Decidir"));
                e=await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,new(ExecutionTaskState.InProgress,e.Revision,e.IncidentRevision,"Iniciar"));
                e=await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Completar"));
                Assert.Equal("COMPLETED",e.Tasks[1].Status);
                var db=scope.ServiceProvider.GetRequiredService<OpenIrpDbContext>();
                foreach(var sql in new[]{"UPDATE app.dependency_exceptions SET impact='Alterado'","DELETE FROM app.dependency_exceptions"})
                    Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql))).SqlState);
            }
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }

    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task DependencyRequestsRequireAssignedUnstartedOrBlockedSuccessors()
    {
        var(f,e)=await DependencySeed();var target=e.Tasks[1].Id;
        e=await Executions(s=>s.AssignAsync(f.Admin,f.Org,f.Incident.Id,e.Id,target,new(null,e.Revision,e.IncidentRevision,"Desasignar")));
        await Rejected(409,()=>AskDependency(f,e),"execution_task_unassigned");
        e=await AssignTask(f,e,target);e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        e=await TaskState(f,e,target,ExecutionTaskState.InProgress);
        await Rejected(409,()=>AskDependency(f,e),"dependency_exception_requires_unstarted_or_blocked_task");
        e=await TaskState(f,e,target,ExecutionTaskState.Completed);
        await Rejected(409,()=>AskDependency(f,e),"dependency_exception_requires_unstarted_or_blocked_task");
    }
    [Fact,Trait("Feature","DependencyExceptions")]
    public async Task SqlDependencyAuthorizationAlsoRequiresAnActiveOrganization()
    {
        var(f,e)=await DependencySeed();e=await AskDependency(f,e);e=await DecideDependency(f,e,await LastDependencyRequest(f,e));
        Task<bool> Granted()=>InScope((_,db)=>db.Database.SqlQuery<bool>($"""SELECT app.has_dependency_exception({e.Tasks[1].Id},{e.Tasks[0].Id},{f.Membership}) AS "Value" """).SingleAsync());
        Assert.True(await Granted());
        await InScope(async(_,db)=>{(await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;return await db.SaveChangesAsync();});
        Assert.False(await Granted());
        await Rejected(403,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress));
    }
}
