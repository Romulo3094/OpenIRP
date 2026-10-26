using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private async Task<T> Evidence<T>(Func<IEvidenceService,Task<T>> operation)
    { using var scope=app.Services.CreateScope(); return await operation(scope.ServiceProvider.GetRequiredService<IEvidenceService>()); }
    private async Task<EvidenceResult> AddEvidence(ExecutionFixture f,ExecutionItem? run=null,EvidenceKind kind=EvidenceKind.Text,Guid? actor=null)
    {
        var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        return await Evidence(s=>s.RegisterAsync(actor??f.Admin,f.Org,f.Incident.Id,new(kind,EvidenceClassification.Confidential,
            "Registro operativo",kind==EvidenceKind.Text ? "Contenido privado de prueba" : "https://example.test/case/123",
            run?.Id,run?.Tasks[0].Id,parent.Revision,"Registrar evidencia pertinente")));
    }
    private async Task<(ExecutionFixture F,ExecutionItem E)> EvidenceSetup(bool approval=false)
    {
        var f=await ExecutionSeed(controls:!approval,approvals:approval,approvalEvidence:approval);
        var e=await BeginExecution(f); e=await AssignTask(f,e,e.Tasks[0].Id);
        if(approval){ e=await AskStep(f,e); e=await DecideStep(f,e,await LastStepRequest(f,e)); }
        e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);
        return (f,e);
    }
    private Task<ExecutionItem> CompleteWithEvidence(ExecutionFixture f,ExecutionItem e,Guid? evidence)=>
        Executions(s=>s.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,
            new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Completar con soporte explícito",evidence)));

    [Theory,Trait("Feature","Evidence")]
    [InlineData(EvidenceKind.Text)]
    [InlineData(EvidenceKind.Reference)]
    public async Task EvidenceRegistrationPreservesPrivateContentAndCorrelatedHistory(EvidenceKind kind)
    {
        var f=await ExecutionSeed(); var created=await AddEvidence(f,kind:kind);
        Assert.Equal(4,created.IncidentRevision); Assert.Equal("ACTIVE",created.Evidence.Status); Assert.Equal(1,created.Evidence.Revision);
        Assert.NotNull(created.Evidence.Content);
        var page=await Evidence(s=>s.ListEvidenceAsync(f.Admin,f.Org,f.Incident.Id));
        Assert.Null(Assert.Single(page.Items).Content);
        var item=await Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,created.Evidence.Id));
        Assert.Equal(created.Evidence,item);
        var history=await Evidence(s=>s.EvidenceEventsAsync(f.Admin,f.Org,f.Incident.Id,item.Id));
        var first=Assert.Single(history);
        var inc=(await Incidents(s=>s.EventsAsync(f.Admin,f.Org,f.Incident.Id))).Items.Last();
        Assert.Equal("EVIDENCE_REGISTERED",inc.Action); Assert.Equal(first.CorrelationId,inc.CorrelationId);
        Assert.DoesNotContain(item.Content!,inc.Details.GetRawText());
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredEvidenceCompletionPinsExactTaskEvidenceAndWithdrawalRedactsIt(bool approval)
    {
        var (f,e)=await EvidenceSetup(approval); var row=await AddEvidence(f,e);
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        e=await CompleteWithEvidence(f,e,row.Evidence.Id);
        Assert.Equal(row.Evidence.Id,e.Tasks[0].CompletionEvidenceId);
        var revision=e.IncidentRevision;
        var withdrawn=await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Retirar contenido del acceso operativo")));
        Assert.Equal(revision,withdrawn.IncidentRevision); Assert.Equal("WITHDRAWN",withdrawn.Evidence.Status);
        Assert.Null(withdrawn.Evidence.Content); Assert.Null(withdrawn.Evidence.Title);
        var final=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal("COMPLETED",final.Tasks[0].Status); Assert.Equal(row.Evidence.Id,final.Tasks[0].CompletionEvidenceId);
        var history=await Evidence(s=>s.EvidenceEventsAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id));
        Assert.Equal(new[]{"REGISTERED","WITHDRAWN"},history.Select(h=>h.Action));
        await Rejected(409,()=>Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Token anterior"))),"stale_evidence_revision");
        await Rejected(409,()=>Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(2,"No restaurar"))),"evidence_already_withdrawn");
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData("incident")]
    [InlineData("other_task")]
    [InlineData("withdrawn")]
    [InlineData("missing")]
    public async Task CompletionCannotUseMissingWithdrawnOrUnrelatedEvidence(string invalid)
    {
        var (f,e)=await EvidenceSetup();
        var row=await AddEvidence(f,invalid=="incident" ? null:e);
        if(invalid=="withdrawn") await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Retirada antes de completar")));
        if(invalid=="other_task")
        {
            var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
            row=await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Internal,"Otra tarea","Soporte de otra tarea",
                e.Id,e.Tasks[1].Id,parent.Revision,"Documentar otra tarea")));
        }
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        await Rejected(409,()=>CompleteWithEvidence(f,e,invalid=="missing" ? Guid.NewGuid():row.Evidence.Id),"active_task_evidence_required");
        Assert.Equal("IN_PROGRESS",(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Tasks[0].Status);
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData("file")]
    [InlineData("restricted")]
    [InlineData("invalid_enum")]
    [InlineData("empty")]
    [InlineData("http")]
    [InlineData("credentials")]
    [InlineData("partial_scope")]
    public async Task EvidenceRejectsUnsupportedPoliciesAndMalformedContent(string invalid)
    {
        var f=await ExecutionSeed();
        var request=new RegisterEvidence(EvidenceKind.Text,EvidenceClassification.Internal,"Título","Contenido",null,null,3,"Registro");
        request=invalid switch {
            "file"=>request with{Kind=EvidenceKind.File},
            "restricted"=>request with{Classification=EvidenceClassification.Restricted},
            "invalid_enum"=>request with{Kind=(EvidenceKind)999},
            "empty"=>request with{Content=" "},
            "http"=>request with{Kind=EvidenceKind.Reference,Content="http://example.test"},
            "credentials"=>request with{Kind=EvidenceKind.Reference,Content="https://user:secret@example.test"},
            _=>request with{TaskId=Guid.NewGuid()} };
        await Rejected(invalid is "file" or "restricted" ? 409:400,()=>Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,request)));
        Assert.Equal(0,await InScope((_,db)=>db.IncidentEvidence.CountAsync()));
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task EvidencePermissionsDoNotGrantGlobalOrNonAdminWithdrawal(OrganizationRole role)
    {
        var f=await ExecutionSeed(); var row=await AddEvidence(f);
        await Rejected(403,()=>Evidence(s=>s.GetEvidenceAsync(f.Platform,f.Org,f.Incident.Id,row.Evidence.Id)));
        await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role)));
        Assert.NotNull((await Evidence(s=>s.GetEvidenceAsync(f.Platform,f.Org,f.Incident.Id,row.Evidence.Id))).Content);
        await Rejected(403,()=>Evidence(s=>s.WithdrawAsync(f.Platform,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"No administra"))));
        if(role==OrganizationRole.Viewer) await Rejected(403,()=>AddEvidence(f,actor:f.Platform));
        else Assert.Equal(f.Platform,(await AddEvidence(f,actor:f.Platform)).Evidence.CreatedByUserId);
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData("account")]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("lockout")]
    public async Task EvidenceReadsAndWritesRecheckInactiveContext(string invalid)
    {
        var f=await ExecutionSeed(); var row=await AddEvidence(f);
        await InScope(async(_,db)=>
        {
            if(invalid=="organization") (await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;
            else if(invalid=="membership") (await db.OrganizationMemberships.SingleAsync()).Status=MembershipStatus.Suspended;
            else {var u=await db.Users.SingleAsync(u=>u.Id==f.Admin); if(invalid=="account")u.IsActive=false;else u.LockoutEnd=DateTimeOffset.UtcNow.AddHours(1);}
            return await db.SaveChangesAsync();
        });
        await Rejected(403,()=>Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id)));
        await Rejected(403,()=>Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Contexto inactivo"))));
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task EvidenceCannotMixOrganizationsIncidentsOrTaskIdsAndChecksPagination()
    {
        var f=await ExecutionSeed(); var e=await BeginExecution(f); var row=await AddEvidence(f,e);
        var other=await RegisterIncident(f.Admin,f.Org);
        await Rejected(404,()=>Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,other.Id,row.Evidence.Id)));
        await Rejected(404,()=>Evidence(s=>s.RegisterAsync(f.Admin,f.Org,other.Id,new(EvidenceKind.Text,EvidenceClassification.Internal,"Fuera de ámbito","Contenido",e.Id,e.Tasks[0].Id,other.Revision,"Intento"))));
        var beta=await Act(s=>s.CreateOrganizationAsync(f.Platform,new("Beta","beta","CL",Account("beta"))));
        await Rejected(403,()=>Evidence(s=>s.GetEvidenceAsync(f.Admin,beta.Id,f.Incident.Id,row.Evidence.Id)));
        await Rejected(400,()=>Evidence(s=>s.ListEvidenceAsync(f.Admin,f.Org,f.Incident.Id,0,101)));
        await Rejected(400,()=>Evidence(s=>s.EvidenceEventsAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,-1)));
        Assert.Empty(await Evidence(s=>s.EvidenceEventsAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,1)));
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData("register","audit")]
    [InlineData("register","events")]
    [InlineData("withdraw","audit")]
    [InlineData("withdraw","events")]
    [InlineData("complete","audit")]
    public async Task EvidenceFailuresRollBackContentHistoriesAndRevisions(string operation,string sink)
    {
        var (f,e)=await EvidenceSetup(); EvidenceResult? row=null;
        if(operation!="register") row=await AddEvidence(f,e);
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        var count=await InScope((_,db)=>db.IncidentEvidence.CountAsync());
        var table=sink=="audit" ? "audit.audit_events":"app.evidence_events";
        var script="CREATE FUNCTION app.fail_evidence_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected evidence failure'; END; $body$; CREATE TRIGGER fail_evidence_test BEFORE INSERT ON "+table+" FOR EACH ROW "+(sink=="audit" ? "WHEN (NEW.outcome='SUCCESS') ":"")+"EXECUTE FUNCTION app.fail_evidence_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(script));
        await Assert.ThrowsAsync<DbUpdateException>(async()=>
        {
            if(operation=="register") await AddEvidence(f,e);
            else if(operation=="withdraw")await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row!.Evidence.Id,new(1,"Retirar")));
            else await CompleteWithEvidence(f,e,row!.Evidence.Id);
        });
        Assert.Equal(count,await InScope((_,db)=>db.IncidentEvidence.CountAsync()));
        var after=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal(e.Revision,after.Revision); Assert.Equal(e.IncidentRevision,after.IncidentRevision);
        if(row is not null) Assert.Equal("ACTIVE",(await Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id))).Status);
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData("content")]
    [InlineData("delete")]
    [InlineData("events")]
    [InlineData("unproven_completion")]
    public async Task DatabasePreventsEvidenceHistoryLossAndUnprovenCompletion(string operation)
    {
        var (f,e)=await EvidenceSetup(); await AddEvidence(f,e);
        var sql=operation switch{
            "content"=>"UPDATE app.incident_evidence SET content='Alterado'",
            "delete"=>"DELETE FROM app.incident_evidence",
            "events"=>"DELETE FROM app.evidence_events",
            _=>"UPDATE app.execution_tasks SET status='COMPLETED',changed_at_revision=changed_at_revision+1 WHERE status='IN_PROGRESS'"};
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal(operation is "content" or "unproven_completion" ? "23514":"42501",ex.SqlState);
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task WithdrawalRemainsAvailableAfterIncidentClosureWithoutRewritingClosureDecision()
    {
        var f=await ExecutionSeed(); var row=await AddEvidence(f); var inc=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        inc=await CompleteSeverityInputs(f.Admin,f.Org,inc,ImpactLevel.High,IncidentCriticality.High);
        inc=await Incidents(s=>s.AssignAsync(f.Admin,f.Org,inc.Id,new(f.Membership,"Responsable",inc.Revision)));
        inc=await MoveWorkflow(f.Admin,f.Org,inc,IncidentState.Resolved,"Resolución");
        inc=await Incidents(s=>s.RequestClosureAsync(f.Admin,f.Org,inc.Id,new("Revisión de cierre",inc.Revision)));
        // Use the existing workflow helper contract for high-severity approval below.
        var closed=await Incidents(s=>s.DecideClosureAsync(f.Admin,f.Org,inc.Id,inc.ClosureReview!.Id,
            new(true,true,"Administrador único","Cierre",inc.Revision)));
        await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Restringir contenido tras cierre")));
        Assert.Equal(closed,await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id)));
        await Rejected(409,()=>AddEvidence(f),"terminal_incident_requires_reopening");
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task ConcurrentWithdrawalAndCompletionPreserveChronologyAndNeverUseWithdrawnEvidenceFirst()
    {
        var (f,e)=await EvidenceSetup(); var row=await AddEvidence(f,e);
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        async Task<int> Complete(){try{await CompleteWithEvidence(f,e,row.Evidence.Id);return 200;}catch(IdentityOperationException ex){return ex.StatusCode;}}
        async Task<int> Withdraw(){await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Retirada concurrente")));return 200;}
        var results=await Race(Complete,Withdraw); Assert.Equal(200,results[1]); Assert.Contains(results[0],new[]{200,409});
        var after=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal(results[0]==200 ? "COMPLETED":"IN_PROGRESS",after.Tasks[0].Status);
        Assert.Null((await Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id))).Content);
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task EvidenceMigrationPreservesExistingTasksAndGuardsDowngrade()
    {
        var f=await ExecutionSeed(); var e=await BeginExecution(f);
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261024224015_StepApprovals");await db.Database.MigrateAsync();return true;});
        Assert.Equal(3,(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Tasks.Count);
        await AddEvidence(f,e);
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261024224015_StepApprovals");return true;}));
        Assert.Equal(1,await InScope((_,db)=>db.IncidentEvidence.CountAsync()));
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task HttpEvidenceRequiresAuthenticationCsrfExplicitEnumsAndRedactsWithdrawal()
    {
        var f=await ExecutionSeed(); var route=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/evidence";
        using var anon=Client(); Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(route)).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new{title="Sin enums",content="Texto",expectedIncidentRevision=3,reason="Registro"})).StatusCode);
        var response=await admin.PostAsJsonAsync(route,new RegisterEvidence(EvidenceKind.Text,EvidenceClassification.Internal,"Texto","Contenido privado",null,null,3,"Registrar HTTP"));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);var result=(await response.Content.ReadFromJsonAsync<EvidenceResult>())!;
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route+$"/{result.Evidence.Id}/withdrawal",new WithdrawEvidence(1,"Retirar HTTP"))).StatusCode);
        var item=(await admin.GetFromJsonAsync<EvidenceItem>(route+$"/{result.Evidence.Id}"))!;Assert.Null(item.Content);Assert.Null(item.Title);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new RegisterEvidence(EvidenceKind.Text,EvidenceClassification.Internal,"Sin CSRF","Texto",null,null,4,"Registrar"))).StatusCode);
    }

    [Theory,Trait("Feature","Evidence")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentEvidenceRegistrationAndWithdrawalRejectLostRevisions(bool withdrawal)
    {
        var f=await ExecutionSeed(); EvidenceResult? row=withdrawal ? await AddEvidence(f):null;
        var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        async Task<int> Attempt()
        {
            try
            {
                if(withdrawal)await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row!.Evidence.Id,new(1,"Retirada concurrente")));
                else await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Internal,"Registro","Contenido",null,null,parent.Revision,"Registro concurrente")));
                return 200;
            }catch(IdentityOperationException ex){return ex.StatusCode;}
        }
        Assert.Equal(new[]{200,409},(await Race(Attempt,Attempt)).Order().ToArray());
        Assert.Equal(1,await InScope((_,db)=>db.IncidentEvidence.CountAsync()));
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task EvidenceDatabaseRequiresHistoryAndExactTaskEvenForSameIncidentEvidence()
    {
        var (f,e)=await EvidenceSetup(); var row=await AddEvidence(f);
        var sql="UPDATE app.execution_tasks SET status='COMPLETED',completion_evidence_id={0},changed_at_revision=changed_at_revision+1 WHERE id={1}";
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql,row.Evidence.Id,e.Tasks[0].Id)));
        Assert.Equal("23514",ex.SqlState);
        var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async(_,db)=>
        {
            db.IncidentEvidence.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,CreatedByUserId=f.Admin,Title="Sin evento",Content="Contenido"});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact,Trait("Feature","Evidence")]
    public async Task RestrictedRuntimeCanRegisterSelectCompletionAndWithdrawWithoutEditingContent()
    {
        var f=await ExecutionSeed(controls:true);var role="openirp_evidence_runtime_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
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
                using var scope=runtime.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();var evidence=scope.ServiceProvider.GetRequiredService<IEvidenceService>();
                var e=await service.StartAsync(f.Admin,f.Org,f.Incident.Id,new(f.Playbook.Playbook.Id,f.Playbook.VersionId,3,3,"Runtime start"));
                var task=e.Tasks[0].Id;
                e=await service.AssignAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new(f.Membership,e.Revision,e.IncidentRevision,"Runtime assign"));
                e=await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new(ExecutionTaskState.InProgress,e.Revision,e.IncidentRevision,"Runtime begin"));
                var row=await evidence.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Internal,"Soporte","Registro runtime",e.Id,task,e.IncidentRevision,"Runtime evidence"));
                e=await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,task,new(ExecutionTaskState.Completed,e.Revision,row.IncidentRevision,"Runtime complete",row.Evidence.Id));
                Assert.Equal(row.Evidence.Id,e.Tasks[0].CompletionEvidenceId);
                Assert.Null((await evidence.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.Evidence.Id,new(1,"Runtime withdraw"))).Evidence.Content);
                var db=scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach(var query in new[]{"UPDATE app.incident_evidence SET content='Manipulado'","DELETE FROM app.incident_evidence","DELETE FROM app.evidence_events"})
                    Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(query))).SqlState);
            }
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }
}
