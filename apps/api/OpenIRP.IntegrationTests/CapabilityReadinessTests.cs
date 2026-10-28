using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using OpenIRP.Domain.Playbooks;
using OpenIRP.Infrastructure.Persistence;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<ExecutionCapabilityReadiness> Readiness(ExecutionFixture f, ExecutionItem e, Guid? task=null, Guid? actor=null) =>
        Executions(s=>s.CapabilityReadinessAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,task));

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData(CapabilityAvailability.Available,CapabilityRequirementCoverage.DeclaredAvailable,RequiredCapabilityCoverage.DeclaredAvailable)]
    [InlineData(CapabilityAvailability.Unavailable,CapabilityRequirementCoverage.Unavailable,RequiredCapabilityCoverage.Gaps)]
    [InlineData(CapabilityAvailability.Unknown,CapabilityRequirementCoverage.Unknown,RequiredCapabilityCoverage.Unknown)]
    public async Task ReadinessUsesOrganizationDeclarationsWithoutWriting(CapabilityAvailability availability,CapabilityRequirementCoverage coverage,RequiredCapabilityCoverage summary)
    {
        var f=await ExecutionSeed(); var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(availability,"Nota del perfil",0)));
        var before=await InScope((_,db)=>db.AuditEvents.CountAsync());
        var report=await Readiness(f,e);
        Assert.Equal(summary,report.RequiredCoverage);Assert.True(report.RequiresHumanReview);Assert.True(report.HasNotesOrLimitations);
        Assert.Equal(2,report.Requirements.Count);Assert.Equal(1,report.RequiredCount);
        Assert.All(report.Requirements,r=>{Assert.Equal(coverage,r.Coverage);Assert.Equal(1,r.Capability.OrganizationCapabilityRevision);Assert.Equal(1,r.Capability.CatalogRevision);});
        Assert.Equal(e.IncidentRevision,report.IncidentRevision);Assert.Equal(e.Revision,report.ExecutionRevision);
        Assert.Equal(before,await InScope((_,db)=>db.AuditEvents.CountAsync()));
        Assert.Equal(0,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
        Assert.Equal(e.Revision,(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Revision);
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task UndeclaredCapabilityStaysUnknownAndNoProfileIsCreated()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var report=await Readiness(f,e);
        Assert.Equal(RequiredCapabilityCoverage.Unknown,report.RequiredCoverage);Assert.Equal(1,report.RequiredUnknownCount);
        Assert.Equal(1,report.RecommendedUnknownCount);Assert.Equal(0,report.RequiredDeclaredAvailableCount);
        Assert.All(report.Requirements,r=>{Assert.Equal(0,r.Capability.OrganizationCapabilityRevision);Assert.Equal(OpenIRP.Application.Incidents.IncidentCapabilitySource.Undeclared,r.Capability.Source);});
        Assert.Equal(0,await InScope((_,db)=>db.OrganizationCapabilities.CountAsync()));
    }

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData(CapabilityAvailability.Unknown)] [InlineData(CapabilityAvailability.Unavailable)] [InlineData(CapabilityAvailability.Available)]
    public async Task IncidentOverrideAndLimitationsTakePrecedenceAndInheritanceCanBeRestored(CapabilityAvailability availability)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Perfil disponible",0)));
        var change=await Incidents(s=>s.AssessCapabilityAsync(f.Admin,f.Org,f.Incident.Id,capability,Assessment(e.IncidentRevision,availability,1)));
        var report=await Readiness(f,e);
        Assert.Equal(change.IncidentRevision,report.IncidentRevision);
        Assert.All(report.Requirements,r=>{Assert.Equal(availability,r.Capability.EffectiveAvailability);Assert.Equal(OpenIRP.Application.Incidents.IncidentCapabilitySource.IncidentAssessment,r.Capability.Source);Assert.NotNull(r.Capability.AssessmentId);Assert.True(r.HasNotesOrLimitations);});
        await Incidents(s=>s.AssessCapabilityAsync(f.Admin,f.Org,f.Incident.Id,capability,Assessment(change.IncidentRevision,null,1)));
        report=await Readiness(f,e);Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,report.RequiredCoverage);
        Assert.All(report.Requirements,r=>{Assert.Equal(OpenIRP.Application.Incidents.IncidentCapabilitySource.OrganizationProfile,r.Capability.Source);Assert.NotNull(r.Capability.AssessmentId);Assert.NotNull(r.Capability.Limitations);});
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task InactiveCatalogIsAGapEvenIfStoredDeclarationsRemainAvailable()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Disponible antes de retirar catÃƒÂ¡logo",0)));
        await Inventory(s=>s.UpdateCatalogAsync(f.Platform,CatalogKind.Capabilities,capability,new("Retirada","CUSTOM",null,false,1)));
        var report=await Readiness(f,e);
        Assert.Equal(RequiredCapabilityCoverage.Gaps,report.RequiredCoverage);Assert.Equal(1,report.RequiredGapCount);Assert.Equal(1,report.RecommendedGapCount);
        Assert.All(report.Requirements,r=>{Assert.Equal(CapabilityRequirementCoverage.InactiveCatalog,r.Coverage);Assert.Equal(CapabilityAvailability.Available,r.Capability.OrganizationAvailability);Assert.Equal(CapabilityAvailability.Unknown,r.Capability.EffectiveAvailability);Assert.Equal(2,r.Capability.CatalogRevision);});
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task TaskReadinessIncludesGlobalRequirementsAndOnlyItsOwnStep()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        for(var i=0;i<e.Tasks.Count;i++)
        {
            var report=await Readiness(f,e,e.Tasks[i].Id);
            Assert.Equal(e.Tasks[i].Id,report.TaskId);Assert.Equal(e.Tasks[i].Status,report.TaskStatus);
            Assert.Equal(i==1?2:1,report.Requirements.Count);
            Assert.All(report.Requirements,r=>Assert.True(r.StepId is null || r.StepId==e.Tasks[i].StepId));
            Assert.Contains(report.Requirements,r=>r.StepId is null && r.TaskId is null);
            if(i==1){var step=Assert.Single(report.Requirements,r=>r.StepId is not null);Assert.Equal(e.Tasks[i].Id,step.TaskId);Assert.Equal("CONTAIN",step.StepCode);}
        }
        var all=await Readiness(f,e);Assert.Null(all.TaskId);
        Assert.Equal(f.Playbook.Requirements.Select(r=>r.Id).OrderBy(id=>id),all.Requirements.Select(r=>r.RequirementId).OrderBy(id=>id));
    }

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData(false)] [InlineData(true)]
    public async Task NoRequiredCapabilitiesDoesNotImplyPermissionOrTechnicalAdequacy(bool recommendedOnly)
    {
        var f=await ExecutionSeed(customize:c=>c with{Requirements=recommendedOnly?c.Requirements.Select(r=>r with{Level=PlaybookRequirementLevel.Recommended}).ToList():[]});
        var e=await BeginExecution(f);var report=await Readiness(f,e);
        Assert.Equal(RequiredCapabilityCoverage.NoRequiredCapabilities,report.RequiredCoverage);Assert.Equal(0,report.RequiredCount);
        Assert.True(report.RequiresHumanReview);Assert.Equal(recommendedOnly?2:0,report.Requirements.Count);
        Assert.Equal(recommendedOnly?2:0,report.RecommendedUnknownCount);
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task RecommendedGapDoesNotOverrideRequiredCoverage()
    {
        var first=await CapabilityId(); var other=await InScope((_,db)=>db.CapabilityCatalog.Where(c=>c.Id!=first).OrderBy(c=>c.Code).Select(c=>c.Id).FirstAsync());
        var f=await ExecutionSeed(customize:c=>c with{Requirements=c.Requirements.Select(r=>r.StepCode is null?r:r with{CapabilityId=other}).ToList()});
        var e=await BeginExecution(f);
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,f.Playbook.Requirements.Single(r=>r.StepId is null).CapabilityId,new(CapabilityAvailability.Available,null,0)));
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,other,new(CapabilityAvailability.Unavailable,null,0)));
        var report=await Readiness(f,e);Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,report.RequiredCoverage);
        Assert.Equal(1,report.RequiredDeclaredAvailableCount);Assert.Equal(1,report.RecommendedGapCount);
        Assert.True(report.RequiresHumanReview);
    }

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData("withdrawn")] [InlineData("cancelled")] [InlineData("skipped")] [InlineData("completed")]
    public async Task ReadinessRetainsPinnedRequirementsAndShowsCurrentContextForHistoricalExecutions(string state)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        if(state=="withdrawn")await Playbooks(s=>s.WithdrawAsync(f.Platform,null,f.Playbook.Playbook.Id,f.Playbook.VersionId,new("Retirada",3)));
        else if(state=="cancelled")e=await Executions(s=>s.CancelAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Cancelada")));
        else if(state=="completed"){e=await FinishAllTasks(f,e);e=await Executions(s=>s.CompleteAsync(f.Admin,f.Org,f.Incident.Id,e.Id,new(e.Revision,e.IncidentRevision,"Finalizar")));}
        else {e=await AssignTask(f,e,e.Tasks[1].Id);e=await AskSkip(f,e,task:e.Tasks[1].Id);e=await DecideSkip(f,e,await LastSkipRequest(f,e,e.Tasks[1].Id),task:e.Tasks[1].Id);}
        var report=await Readiness(f,e);
        Assert.Equal(f.Playbook.VersionId,report.VersionId);Assert.Equal(2,report.Requirements.Count);
        Assert.Equal(e.Status,report.ExecutionStatus);Assert.Equal(state=="withdrawn"?"ARCHIVED":"PUBLISHED",report.VersionStatus);
        if(state=="skipped")Assert.Equal("SKIPPED",Assert.Single(report.Requirements,r=>r.StepId is not null).TaskStatus);
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,f.Playbook.Requirements[0].CapabilityId,new(CapabilityAvailability.Available,"Cambio posterior",0)));
        Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,(await Readiness(f,e)).RequiredCoverage);
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task ReadinessCannotBypassDependenciesApprovalOrEvidence()
    {
        var f=await ExecutionSeed(approvals:true,approvalEvidence:true);var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,null,0)));
        Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,(await Readiness(f,e)).RequiredCoverage);
        e=await AssignTask(f,e,e.Tasks[0].Id);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress),"task_approval_required");
        e=await AskStep(f,e);e=await DecideStep(f,e,await LastStepRequest(f,e));e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.Completed),"task_evidence_required");
        e=await AssignTask(f,e,e.Tasks[1].Id);
        await Rejected(409,()=>TaskState(f,e,e.Tasks[1].Id,ExecutionTaskState.InProgress),"execution_dependencies_not_completed");
    }

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData("platform")] [InlineData("viewer")] [InlineData("analyst")] [InlineData("inactive-account")] [InlineData("lockout")] [InlineData("suspended-organization")] [InlineData("suspended-member")]
    public async Task ReadinessRevalidatesCurrentMembershipAndAccount(string role)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        if(role=="platform"){await Rejected(403,()=>Readiness(f,e,actor:f.Platform));return;}
        var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role=="analyst"?OrganizationRole.Analyst:OrganizationRole.Viewer)));
        if(role=="inactive-account" || role=="lockout") await InScope(async(_,db)=>{var u=await db.Users.SingleAsync(u=>u.Id==f.Platform);if(role=="lockout")u.LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);else u.IsActive=false;return await db.SaveChangesAsync();});
        else if(role=="suspended-organization")await InScope(async(_,db)=>{(await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;return await db.SaveChangesAsync();});
        else if(role=="suspended-member")await Act(s=>s.ChangeMemberAsync(f.Admin,f.Org,member.Id,new(OrganizationRole.Viewer,MembershipStatus.Suspended,member.UpdatedAt)));
        if(role is "viewer" or "analyst")Assert.Equal(e.Id,(await Readiness(f,e,actor:f.Platform)).ExecutionId);
        else await Rejected(403,()=>Readiness(f,e,actor:f.Platform));
    }

    [Theory, Trait("Feature","CapabilityReadiness")]
    [InlineData("incident")] [InlineData("execution")] [InlineData("task")] [InlineData("organization")]
    public async Task ReadinessRejectsObjectsOutsideTheAuthorizedScope(string missing)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        if(missing=="organization"){await Rejected(403,()=>Executions(s=>s.CapabilityReadinessAsync(f.Admin,Guid.NewGuid(),f.Incident.Id,e.Id)));return;}
        await Rejected(404,()=>Executions(s=>s.CapabilityReadinessAsync(f.Admin,f.Org,missing=="incident"?Guid.NewGuid():f.Incident.Id,
            missing=="execution"?Guid.NewGuid():e.Id,missing=="task"?Guid.NewGuid():null)));
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task HttpReadinessUsesAuthenticationNoStoreAndTypedResponseWithoutCsrfOnGet()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        var root=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}";
        using var anon=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(root+"/capability-readiness")).StatusCode);
        using var admin=Client();await Login(admin,"alpha");admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        foreach(var path in new[]{root+"/capability-readiness",root+$"/tasks/{e.Tasks[0].Id}/capability-readiness"})
        {
            using var response=await admin.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
            var json=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.True(json.GetProperty("requiresHumanReview").GetBoolean());Assert.Equal("Unknown",json.GetProperty("requiredCoverage").GetString());
        }
    }

    private sealed class ReadinessProfileChangeInterceptor(Func<Task> change) : DbCommandInterceptor
    {
        private int changed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {
            if(command.CommandText.Contains("playbook_capability_requirements",StringComparison.Ordinal) && Interlocked.Exchange(ref changed,1)==0)await change();
            return result;
        }
    }
    private sealed class ReadinessApplication(string connection,IInterceptor interceptor) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:OpenIRP"]=connection}));
            builder.ConfigureLogging(logging=>logging.ClearProviders());
            builder.ConfigureServices(services=>services.AddDbContext<OpenIrpDbContext>(options=>options.AddInterceptors(interceptor)));
        }
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task ConcurrentProfileAndCatalogChangesCannotProduceAMixedReadinessSnapshot()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Available,"Antes",0)));
        var calls=0;
        var interceptor=new ReadinessProfileChangeInterceptor(async()=>
        {
            calls++;await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Unavailable,"DespuÃƒÂ©s",1)));
            await Inventory(s=>s.UpdateCatalogAsync(f.Platform,CatalogKind.Capabilities,capability,new("Retirada","CUSTOM",null,false,1)));
        });
        var connection=await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString()!));
        await using var isolated=new ReadinessApplication(connection,interceptor);
        using var scope=isolated.Services.CreateScope();
        var report=await scope.ServiceProvider.GetRequiredService<IExecutionService>().CapabilityReadinessAsync(f.Admin,f.Org,f.Incident.Id,e.Id);
        Assert.Equal(1,calls);Assert.Equal(RequiredCapabilityCoverage.DeclaredAvailable,report.RequiredCoverage);
        Assert.All(report.Requirements,r=>{Assert.Equal(1,r.Capability.CatalogRevision);Assert.Equal(1,r.Capability.OrganizationCapabilityRevision);});
        var latest=await Readiness(f,e);Assert.Equal(RequiredCapabilityCoverage.Gaps,latest.RequiredCoverage);
        Assert.All(latest.Requirements,r=>{Assert.Equal(2,r.Capability.CatalogRevision);Assert.Equal(2,r.Capability.OrganizationCapabilityRevision);});
    }
    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task SharedPublishedRequirementsDoNotMixOrganizationProfilesOrIncidentOverrides()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var capability=f.Playbook.Requirements[0].CapabilityId;
        var beta=await Act(s=>s.CreateOrganizationAsync(f.Platform,new("Beta","beta","CL",Account("beta"))));
        var member=await InScope((_,db)=>db.OrganizationMemberships.SingleAsync(m=>m.OrganizationId==beta.Id));
        var incident=await RegisterIncident(member.UserId,beta.Id);
        incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.UnderEvaluation);
        incident=await MoveWorkflow(member.UserId,beta.Id,incident,IncidentState.InResponse);
        var other=new ExecutionFixture(f.Platform,member.UserId,member.Id,beta.Id,incident,f.Playbook);
        var otherRun=await BeginExecution(other);
        await Inventory(s=>s.PutCapabilityAsync(f.Admin,f.Org,capability,new(CapabilityAvailability.Unavailable,"Privado Alpha",0)));
        await Inventory(s=>s.PutCapabilityAsync(member.UserId,beta.Id,capability,new(CapabilityAvailability.Available,"Privado Beta",0)));
        await Incidents(s=>s.AssessCapabilityAsync(member.UserId,beta.Id,incident.Id,capability,Assessment(otherRun.IncidentRevision,CapabilityAvailability.Unknown,1)));
        var alpha=await Readiness(f,e);var betaReport=await Readiness(other,otherRun);
        Assert.Equal(RequiredCapabilityCoverage.Gaps,alpha.RequiredCoverage);Assert.Equal(RequiredCapabilityCoverage.Unknown,betaReport.RequiredCoverage);
        Assert.All(alpha.Requirements,r=>{Assert.Equal("Privado Alpha",r.Capability.OrganizationNotes);Assert.Null(r.Capability.OverrideAvailability);});
        Assert.All(betaReport.Requirements,r=>{Assert.Equal("Privado Beta",r.Capability.OrganizationNotes);Assert.Equal(CapabilityAvailability.Unknown,r.Capability.OverrideAvailability);});
        await Rejected(404,()=>Executions(s=>s.CapabilityReadinessAsync(member.UserId,beta.Id,f.Incident.Id,e.Id)));
        await Rejected(403,()=>Readiness(f,e,actor:member.UserId));
        await Rejected(404,()=>Readiness(f,e,otherRun.Tasks[0].Id));
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task AllThreeHundredPublishedRequirementsAreReturnedWithoutTruncation()
    {
        var capabilities=await InScope((_,db)=>db.CapabilityCatalog.OrderBy(c=>c.Code).Select(c=>c.Id).ToListAsync());
        var f=await ExecutionSeed(customize:c=>
        {
            var steps=Enumerable.Range(0,40).Select(n=>c.Steps[0] with{Code="STEP_"+n,Position=n+1}).ToList();
            var requirements=steps.SelectMany(step=>capabilities.Select(id=>new OpenIRP.Application.Playbooks.PlaybookRequirementInput(id,step.Code,PlaybookRequirementLevel.Required))).Take(300).ToList();
            return c with{Steps=steps,Dependencies=[],Requirements=requirements};
        });
        var e=await BeginExecution(f);var report=await Readiness(f,e);
        Assert.Equal(300,report.Requirements.Count);Assert.Equal(300,report.RequiredUnknownCount);
        Assert.Equal(300,report.Requirements.Select(r=>r.RequirementId).Distinct().Count());
        Assert.All(report.Requirements,r=>Assert.NotNull(r.TaskId));
        Assert.Equal(RequiredCapabilityCoverage.Unknown,report.RequiredCoverage);
    }

    [Fact, Trait("Feature","CapabilityReadiness")]
    public async Task RestrictedRuntimeCanReadCapabilityCoverageWithoutNewGrantsOrWrites()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var role="openirp_readiness_runtime_"+Guid.NewGuid().ToString("N");
        var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var command=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await command.ExecuteNonQueryAsync();
        try
        {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grants=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grants.ExecuteNonQueryAsync();
            var before=await InScope((_,db)=>db.AuditEvents.CountAsync());builder.Username=role;builder.Password=password;
            await using(var runtime=new TestApplication(builder.ConnectionString))
            {
                using var scope=runtime.Services.CreateScope();
                var report=await scope.ServiceProvider.GetRequiredService<IExecutionService>().CapabilityReadinessAsync(f.Admin,f.Org,f.Incident.Id,e.Id);
                Assert.Equal(RequiredCapabilityCoverage.Unknown,report.RequiredCoverage);Assert.Equal(e.Revision,report.ExecutionRevision);
            }
            Assert.Equal(before,await InScope((_,db)=>db.AuditEvents.CountAsync()));
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }
}
