using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Xunit;
namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    [Theory,Trait("Feature","RestrictedEvidence")]
    [InlineData(OrganizationRole.Analyst,EvidenceKind.Text)] [InlineData(OrganizationRole.Viewer,EvidenceKind.Text)]
    [InlineData(OrganizationRole.Analyst,EvidenceKind.Reference)] [InlineData(OrganizationRole.Viewer,EvidenceKind.Reference)]
    public async Task RestrictedEvidenceIsExclusiveToActiveLocalAdmin(OrganizationRole role,EvidenceKind kind)
    {
        var f=await ExecutionSeed();await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role)));
        var row=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(kind,EvidenceClassification.Restricted,"Restricted title",
            kind==EvidenceKind.Text ? "Restricted body" : "https://example.test/restricted",null,null,f.Incident.Revision,"Registro restringido")));
        Assert.NotNull((await Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id))).Content);
        Assert.Empty((await Evidence(s=>s.ListEvidenceAsync(f.Platform,f.Org,f.Incident.Id))).Items);
        await Rejected(404,()=>Evidence(s=>s.GetEvidenceAsync(f.Platform,f.Org,f.Incident.Id,row.Evidence.Id)));
        await Rejected(404,()=>Evidence(s=>s.EvidenceEventsAsync(f.Platform,f.Org,f.Incident.Id,row.Evidence.Id)));
        if(role==OrganizationRole.Analyst)await Rejected(403,()=>Evidence(s=>s.RegisterAsync(f.Platform,f.Org,f.Incident.Id,new(kind,EvidenceClassification.Restricted,"Secret","Data",null,null,row.IncidentRevision,"Registrar"))));
        Assert.Single((await Evidence(s=>s.ListEvidenceAsync(f.Admin,f.Org,f.Incident.Id))).Items);
    }
    [Fact,Trait("Feature","RestrictedEvidence")]
    public async Task RestrictedCompletionCannotBeUsedByAnalystAndReadinessRedactsMetadata()
    {
        var(f,e)=await EvidenceSetup();var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        var evidence=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Restricted,"Private title","Private content",e.Id,e.Tasks[0].Id,parent.Revision,"Registro")));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        var report=await Support(f,e,f.Platform);var row=report.Tasks[0];
        Assert.Equal(TaskEvidenceSupportState.AccessRestricted,row.SupportState);Assert.Null(row.CompletionEvidenceId);Assert.Null(row.EvidenceKind);Assert.Null(row.EvidenceRevision);
        Assert.Equal(TaskEvidenceSupportState.MetadataAvailable,(await Support(f,e)).Tasks[0].SupportState);
    }
    [Theory,Trait("Feature","RestrictedEvidence")]
    [InlineData(OrganizationRole.Analyst)] [InlineData(OrganizationRole.Viewer)]
    public async Task RestrictedFilesDenyMetadataEventsAndBytes(OrganizationRole role)
    {
        var f=await ExecutionSeed();await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role)));
        await WithFiles(async(runtime,root)=>{
            var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));await using var input=new MemoryStream(Encoding.UTF8.GetBytes("Contenido restringido"));
            var upload=await Files(runtime,s=>s.UploadAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceClassification.Restricted,"Archivo",null,null,parent.Revision,"Registrar"),input,"secret.txt","text/plain"));
            var id=upload.File.EvidenceId;
            await Rejected(404,()=>Files(runtime,s=>s.FileMetadataAsync(f.Platform,f.Org,f.Incident.Id,id)));
            await Rejected(404,()=>Files(runtime,s=>s.FileEventsAsync(f.Platform,f.Org,f.Incident.Id,id)));
            await Rejected(404,()=>Files(runtime,s=>s.DownloadAsync(f.Platform,f.Org,f.Incident.Id,id)));
            var download=await Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,id));await download.Content.DisposeAsync();
            Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
        });
    }
    [Theory,Trait("Feature","FileVerification")]
    [InlineData("valid")] [InlineData("tampered")] [InlineData("missing")] [InlineData("withdrawn")]
    public async Task AdministrativeFileVerificationIsAuditableAndNeverPurgesOrReleasesQuarantine(string scenario)
    {
        var f=await ExecutionSeed();await WithFiles(async(runtime,root)=>{
            var upload=await UploadFile(runtime,f);var id=upload.File.EvidenceId;var path=Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
            if(scenario=="tampered")await File.WriteAllTextAsync(path,"Alterado");
            if(scenario=="missing")File.Delete(path);
            if(scenario=="withdrawn")await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,id,new(1,"Retener sin acceso")));
            var before=await Files(runtime,s=>s.FileMetadataAsync(f.Admin,f.Org,f.Incident.Id,id));
            var check=await Files(runtime,s=>s.VerifyFileAsync(f.Admin,f.Org,f.Incident.Id,id,new(before.Revision,"Diagnostico administrativo")));
            Assert.Equal(scenario is "valid" or "withdrawn",check.PhysicalIntegrityVerified);Assert.Equal(scenario=="valid",check.DownloadAllowed);
            Assert.False(check.MalwareScanned);Assert.False(check.TechnicalSufficiencyVerified);
            Assert.Equal(scenario=="valid" ? "AVAILABLE":"QUARANTINED",check.State);
            Assert.Equal(scenario!="missing",File.Exists(path));
            Assert.True(await InScope((_,db)=>db.AuditEvents.AnyAsync(a=>a.Action=="evidence.file.verification_result" && a.ResourceId==id)));
            if(scenario is "missing" or "tampered")await Rejected(409,()=>Files(runtime,s=>s.VerifyFileAsync(f.Admin,f.Org,f.Incident.Id,id,new(before.Revision,"Version antigua"))));
        });
    }
    [Fact,Trait("Feature","FileVerification")]
    public async Task FileVerificationRequiresLocalAdminAndDoesNotCreateSuccessOnBadRevision()
    {
        var f=await ExecutionSeed();await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        await WithFiles(async(runtime,root)=>{
            var row=await UploadFile(runtime,f);var id=row.File.EvidenceId;
            await Rejected(403,()=>Files(runtime,s=>s.VerifyFileAsync(f.Platform,f.Org,f.Incident.Id,id,new(2,"Revisar"))));
            await Rejected(409,()=>Files(runtime,s=>s.VerifyFileAsync(f.Admin,f.Org,f.Incident.Id,id,new(1,"Revision antigua"))));
            Assert.False(await InScope((_,db)=>db.AuditEvents.AnyAsync(a=>a.Action=="evidence.file.verification_result" && a.ResourceId==id)));
        });
    }
    [Theory,Trait("Feature","DerivedSecurity")]
    [InlineData(1,true)] [InlineData(2,true)] [InlineData(1,false)] [InlineData(2,false)]
    public async Task SecurityAlertsPropagateTransitivelyWithGenericNoticeAndIndependentDecision(int depth,bool proceed)
    {
        var source=await ExecutionSeed();var destination=source;
        for(var n=0;n<depth;n++){
            var clone=await Playbooks(s=>s.CloneAsync(source.Admin,source.Org,CloneRequest(destination.Playbook)));
            clone=await DecidePlaybook(source.Admin,source.Org,await SubmitPlaybook(source.Admin,source.Org,clone));destination=destination with{Playbook=clone};
        }
        var e=await BeginExecution(destination);e=await AssignTask(destination,e,e.Tasks[0].Id);
        var before=await ClosureRead(source.Admin,source.Org,source.Incident.Id);var root=await Playbooks(s=>s.WithdrawForSecurityAsync(source.Platform,null,source.Playbook.Playbook.Id,source.Playbook.VersionId,new("PRIVATE_SOURCE_DETAIL_UNIQUE",3)));
        var state=await HoldState(destination,e);Assert.True(state.CurrentHold!.IsInherited);Assert.Equal(e.VersionId,state.CurrentHold.VersionId);Assert.Equal(Guid.Empty,state.CurrentHold.ActorUserId);
        Assert.DoesNotContain(root.Reason,JsonSerializer.Serialize(state.CurrentHold));Assert.False(state.ContinuityAuthorized);
        Assert.NotEqual(before.RiskReview.ContextToken,(await ClosureRead(source.Admin,source.Org,source.Incident.Id)).RiskReview.ContextToken);
        await Rejected(409,()=>TaskState(destination,e,e.Tasks[0].Id,ExecutionTaskState.InProgress));
        e=await DecideHold(destination,e,root.Id,proceed);
        if(proceed){Assert.True((await HoldState(destination,e)).ContinuityAuthorized);e=await TaskState(destination,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);}
        else Assert.Equal("CANCELLED",e.Status);
        Assert.Single(await Executions(s=>s.SecurityDecisionsAsync(source.Admin,source.Org,source.Incident.Id,e.Id)));
    }
    [Fact,Trait("Feature","DerivedSecurity")]
    public async Task NewAncestorAlertInvalidatesOldDecisionAndBlocksNewStartsWithoutCopyingPrivateContent()
    {
        var source=await ExecutionSeed();var clone=await Playbooks(s=>s.CloneAsync(source.Admin,source.Org,CloneRequest(source.Playbook)));
        clone=await DecidePlaybook(source.Admin,source.Org,await SubmitPlaybook(source.Admin,source.Org,clone));var f=source with{Playbook=clone};
        var e=await BeginExecution(f);var first=await HoldVersion(source);e=await DecideHold(f,e,first.Id);
        var second=await HoldVersion(source,4);Assert.False((await HoldState(f,e)).ContinuityAuthorized);await Rejected(409,()=>DecideHold(f,e,first.Id));
        e=await DecideHold(f,e,second.Id,false);
        f=f with{Incident=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))};await Rejected(409,()=>BeginExecution(f),"security_held_source");
        Assert.Equal(2,await InScope((_,db)=>db.PlaybookSecurityHolds.CountAsync()));
    }
    [Fact,Trait("Feature","RestrictedEvidence")]
    public async Task AnalystCannotSelectRestrictedEvidenceEvenWhenAssignedToItsExactTask()
    {
        var f=await ExecutionSeed(controls:true);var e=await BeginExecution(f);
        var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));
        e=await AssignTask(f,e,e.Tasks[0].Id,member.Id);e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress,f.Platform);
        var row=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Restricted,"Privado","Contenido",e.Id,e.Tasks[0].Id,e.IncidentRevision,"Registrar soporte")));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        await Rejected(404,()=>Executions(s=>s.ChangeAsync(f.Platform,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Intentar evidencia",row.Evidence.Id))));
        var unchanged=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(e.Revision,unchanged.Revision);Assert.Null(unchanged.Tasks[0].CompletionEvidenceId);
    }
    [Fact,Trait("Feature","DerivedSecurity")]
    public async Task SqlCannotAdvanceHeldDerivedTaskAndAnotherExecutionNeedsItsOwnDecision()
    {
        var source=await ExecutionSeed();var clone=await Playbooks(s=>s.CloneAsync(source.Admin,source.Org,CloneRequest(source.Playbook)));
        clone=await DecidePlaybook(source.Admin,source.Org,await SubmitPlaybook(source.Admin,source.Org,clone));var f=source with{Playbook=clone};
        var e=await BeginExecution(f);e=await AssignTask(f,e,e.Tasks[0].Id);
        var other=await RegisterIncident(f.Admin,f.Org);other=await MoveWorkflow(f.Admin,f.Org,other,IncidentState.UnderEvaluation);other=await MoveWorkflow(f.Admin,f.Org,other,IncidentState.InResponse);
        var g=f with{Incident=other};var second=await BeginExecution(g);second=await AssignTask(g,second,second.Tasks[0].Id);
        var hold=await HoldVersion(source);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.execution_tasks SET status='IN_PROGRESS',changed_at_revision=changed_at_revision+1 WHERE id={e.Tasks[0].Id}")))).SqlState);
        e=await DecideHold(f,e,hold.Id);Assert.True((await HoldState(f,e)).ContinuityAuthorized);Assert.False((await HoldState(g,second)).ContinuityAuthorized);
        await Rejected(409,()=>TaskState(g,second,second.Tasks[0].Id,ExecutionTaskState.InProgress));
    }

}
