using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenIRP.Application.Assistance;
using OpenIRP.Application.Executions;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Assistance;
using Xunit;
namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private sealed class FakeAssistant(Func<AssistanceInput,Task<LocalAssistantReply>>? act=null):ILocalAssistant
    {
        public int Calls;public AssistanceInput? Captured;
        public Task<LocalAssistantReply> ExplainAsync(AssistanceInput input,CancellationToken ct=default){Calls++;Captured=input;return act is null?Task.FromResult(Healthy(input)):act(input);}
    }
    private static LocalAssistantReply Healthy(AssistanceInput input)=>new("qwen3:4b-instruct",new string('a',64),"VALID",null,new(input.StepCode,"Orientacion revisable para este paso",["Mantener aprobaciones y evidencia"],[]));
    private async Task WithAssistant(ILocalAssistant? client,Func<WebApplicationFactory<Program>,Task> action,string? runtimeConnection=null)
    {
        var connection=await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString()));
        await using var runtime=app.WithWebHostBuilder(b=>{
            b.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:OpenIRP"]=runtimeConnection??connection,["LocalAI:Enabled"]="true",["LocalAI:TimeoutSeconds"]="90",["LocalAI:Model"]="qwen3:4b-instruct",["LocalAI:BaseUrl"]="http://127.0.0.1:11435"}));
            if(client is not null)b.ConfigureServices(s=>{s.RemoveAll<ILocalAssistant>();s.AddSingleton(client);});
        });
        await action(runtime);
    }
    private static async Task<T> Ai<T>(WebApplicationFactory<Program> runtime,Func<IAssistanceService,Task<T>> action)
    {using var scope=runtime.Services.CreateScope();return await action(scope.ServiceProvider.GetRequiredService<IAssistanceService>());}
    private Task<AssistanceContext> AiContext(WebApplicationFactory<Program> runtime,ExecutionFixture f,ExecutionItem e,Guid? actor=null)=>
        Ai(runtime,s=>s.AssistanceContextAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id));
    private async Task<AssistanceAssessmentItem> Explain(WebApplicationFactory<Program> runtime,ExecutionFixture f,ExecutionItem e,ExplainStep? command=null)
    {var c=await AiContext(runtime,f,e);return await Ai(runtime,s=>s.ExplainStepAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,command??new(c.IncidentRevision,c.ExecutionRevision,c.ContextToken,"EXPLAIN_NOT_FOR_MODEL")));}
    private Task<AssistanceAssessmentPage> AiHistory(WebApplicationFactory<Program> runtime,ExecutionFixture f,ExecutionItem e)=>
        Ai(runtime,s=>s.AssistanceHistoryAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id));
    [Fact,Trait("Feature","LocalAi")]
    public async Task StepAssistanceUsesMinimalContextAndNeverChangesManualWorkflow()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        await Evidence(s=>s.RegisterAsync(f.Admin,f.Org,f.Incident.Id,new(EvidenceKind.Text,EvidenceClassification.Restricted,"PRIVATE_TITLE","PRIVATE_RESTRICTED_BODY",null,null,e.IncidentRevision,"Registrar privado")));
        e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));var fake=new FakeAssistant();
        var real=Environment.GetEnvironmentVariable("OPENIRP_TEST_LOCAL_AI")=="true";
        await WithAssistant(real?null:fake,async runtime=>{
            var auditBefore=await InScope((_,db)=>db.AuditEvents.CountAsync());var c=await AiContext(runtime,f,e);Assert.Empty((await AiHistory(runtime,f,e)).Items);
            Assert.Equal(auditBefore,await InScope((_,db)=>db.AuditEvents.CountAsync()));
            var row=await Explain(runtime,f,e);if(real)Console.WriteLine($"REAL_LOCAL_AI_RESULT outcome={row.Outcome} error={row.ErrorCode} durationMs={row.DurationMs}");Assert.Equal("VALID",row.Outcome);Assert.True(row.IsCurrent);Assert.NotNull(row.Recommendation);Assert.False(row.OriginalResponseStored);Assert.False(row.AuthorizesExecution);Assert.True(row.RequiresHumanReview);
            var sent=JsonSerializer.Serialize(real?c.Input:fake.Captured,OllamaLocalAssistant.Json);
            foreach(var secret in new[]{"PRIVATE_RESTRICTED_BODY","PRIVATE_TITLE","EXPLAIN_NOT_FOR_MODEL",f.Admin.ToString(),f.Org.ToString(),e.Id.ToString(),"@example.test"})Assert.DoesNotContain(secret,sent);
            var current=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));Assert.Equal(e.Revision,current.Revision);Assert.Equal(e.IncidentRevision,current.IncidentRevision);Assert.Equal(e.Tasks[0].Status,current.Tasks[0].Status);
            Assert.Equal(c.ContextToken,(await AiContext(runtime,f,e)).ContextToken);Assert.Single((await AiHistory(runtime,f,e)).Items);
            Assert.Equal(auditBefore+1,await InScope((_,db)=>db.AuditEvents.CountAsync()));
            if(real)Console.WriteLine($"REAL_LOCAL_AI_VERIFIED model={row.Model} digest={row.ModelDigest} durationMs={row.DurationMs} assessment={row.Id}");
        });
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("UNAVAILABLE")] [InlineData("INVALID")]
    public async Task FailedInferenceIsAuditableAndManualTasksStillWork(string outcome)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var fake=new FakeAssistant(_=>Task.FromResult(new LocalAssistantReply("qwen3:4b-instruct",null,outcome,"ai_unavailable",null)));
        await WithAssistant(fake,async runtime=>{var result=await Explain(runtime,f,e);Assert.Equal(outcome,result.Outcome);Assert.Null(result.Recommendation);Assert.False(result.IsCurrent);Assert.Single((await AiHistory(runtime,f,e)).Items);});
        e=await AssignTask(f,e,e.Tasks[0].Id);e=await TaskState(f,e,e.Tasks[0].Id,ExecutionTaskState.InProgress);Assert.Equal("IN_PROGRESS",e.Tasks[0].Status);
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("viewer")] [InlineData("unassigned")] [InlineData("global")]
    public async Task AssistanceRequiresAssignedLocalWriterOrLocalAdmin(string role)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var fake=new FakeAssistant();
        if(role!="global")await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role=="viewer"?OrganizationRole.Viewer:OrganizationRole.Analyst)));
        await WithAssistant(fake,async runtime=>{await Rejected(403,()=>AiContext(runtime,f,e,f.Platform));Assert.Equal(0,fake.Calls);});
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task AssignedAnalystCanRequestExplanationWithoutGainingDecisionPermissions()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var member=await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Analyst)));e=await AssignTask(f,e,e.Tasks[0].Id,member.Id);
        await WithAssistant(new FakeAssistant(),async runtime=>{
            var c=await AiContext(runtime,f,e,f.Platform);var r=await Ai(runtime,s=>s.ExplainStepAsync(f.Platform,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(c.IncidentRevision,c.ExecutionRevision,c.ContextToken,"Solicitar explicacion")));
            Assert.Equal("VALID",r.Outcome);Assert.False(r.AuthorizesExecution);
        });
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("incident")] [InlineData("execution")] [InlineData("context")]
    public async Task StalePreconditionsNeverInvokeModel(string stale)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var fake=new FakeAssistant();await WithAssistant(fake,async runtime=>{
            var c=await AiContext(runtime,f,e);var request=new ExplainStep(c.IncidentRevision,c.ExecutionRevision,c.ContextToken,"Consultar");
            request=stale switch{"incident"=>request with{ExpectedIncidentRevision=1},"execution"=>request with{ExpectedExecutionRevision=0},_=>request with{ExpectedContext="old"}};
            await Rejected(409,()=>Explain(runtime,f,e,request));Assert.Equal(0,fake.Calls);Assert.Empty((await AiHistory(runtime,f,e)).Items);
        });
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("hold")] [InlineData("assignment")] [InlineData("withdrawal")]
    public async Task ContextChangesDuringInferenceDiscardRecommendationAndRecordStale(string change)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var evidence=await AddEvidence(f);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        var fake=new FakeAssistant(async input=>{
            if(change=="hold")await HoldVersion(f);
            else if(change=="assignment")await AssignTask(f,e,e.Tasks[0].Id);
            else await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,evidence.Evidence.Id,new(1,"Cambio de contexto")));
            return Healthy(input);
        });
        await WithAssistant(fake,async runtime=>{var result=await Explain(runtime,f,e);Assert.Equal("STALE",result.Outcome);Assert.Null(result.Recommendation);Assert.False(result.IsCurrent);Assert.Single((await AiHistory(runtime,f,e)).Items);});
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("role")] [InlineData("inactive")]
    public async Task AuthorizationIsRecheckedAfterInferenceBeforeSavingOrReturningData(string change)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.OrgAdmin)));
        var fake=new FakeAssistant(async input=>{await InScope(async(_,db)=>{if(change=="role"){var m=await db.OrganizationMemberships.SingleAsync(m=>m.Id==f.Membership);m.Role=OrganizationRole.Viewer;}else{var u=await db.Users.SingleAsync(u=>u.Id==f.Admin);u.IsActive=false;}await db.SaveChangesAsync();return true;});return Healthy(input);});
        await WithAssistant(fake,async runtime=>{await Rejected(403,()=>Explain(runtime,f,e));Assert.Equal(1,fake.Calls);Assert.Equal(0,await InScope((_,db)=>db.AssistanceAssessments.CountAsync()));});
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("incident")] [InlineData("execution")] [InlineData("task")]
    public async Task ContextScopeCannotBeBorrowedFromAnotherResource(string other)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var fake=new FakeAssistant();await WithAssistant(fake,async runtime=>{
            await Rejected(404,()=>Ai(runtime,s=>s.AssistanceContextAsync(f.Admin,f.Org,other=="incident"?Guid.NewGuid():f.Incident.Id,other=="execution"?Guid.NewGuid():e.Id,other=="task"?Guid.NewGuid():e.Tasks[0].Id)));Assert.Equal(0,fake.Calls);
        });
    }
    [Theory,Trait("Feature","LocalAi")]
    [InlineData("update")] [InlineData("delete")] [InlineData("truncate")]
    public async Task AssessmentHistoryIsAppendOnlyInEfAndSql(string operation)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);await WithAssistant(new FakeAssistant(),async runtime=>{await Explain(runtime,f,e);});
        await InScope(async(_,db)=>{var row=await db.AssistanceAssessments.SingleAsync();row.Model="changed";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
        var sql=operation switch{"update"=>"UPDATE app.assistance_assessments SET model='changed'","delete"=>"DELETE FROM app.assistance_assessments",_=>"TRUNCATE app.assistance_assessments"};
        Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)))).SqlState);
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task AuditFailureRollsBackAssessmentAndDoesNotChangeAggregates()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        await InScope(async(_,db)=>{await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION audit.fail_ai() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.action='ai.assistance.evaluated' THEN RAISE EXCEPTION 'Injected audit failure'; END IF; RETURN NEW; END;$$; CREATE TRIGGER fail_ai BEFORE INSERT ON audit.audit_events FOR EACH ROW EXECUTE FUNCTION audit.fail_ai()");return true;});
        await WithAssistant(new FakeAssistant(),async runtime=>{await Assert.ThrowsAsync<DbUpdateException>(()=>Explain(runtime,f,e));});
        Assert.Equal(0,await InScope((_,db)=>db.AssistanceAssessments.CountAsync()));Assert.Equal(e.Revision,(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Revision);
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task ContextAndExplainHttpRequireAuthenticationCsrfAndExplicitRevisions()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);await WithAssistant(new FakeAssistant(),async runtime=>{
            var path=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/tasks/{e.Tasks[0].Id}";
            using var anonymous=runtime.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path+"/assistance-context")).StatusCode);
            using var admin=runtime.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});await Login(admin,"alpha");
            var c=await AiContext(runtime,f,e);var req=new ExplainStep(c.IncidentRevision,c.ExecutionRevision,c.ContextToken,"Explicar");
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(path+"/assistance",new{expectedContext=c.ContextToken,reason="Faltan revisiones"})).StatusCode);
            var csrf=admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(path+"/assistance",req)).StatusCode);
            admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(path+"/assistance",req)).StatusCode);
            using var response=await admin.GetAsync(path+"/assistance-assessments");Assert.True(response.Headers.CacheControl!.NoStore);
        });
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task HistoryIsPaginatedAndBecomesStaleAfterManualContextChange()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var fake=new FakeAssistant();await WithAssistant(fake,async runtime=>{
            var first=await Explain(runtime,f,e);var second=await Explain(runtime,f,e);
            var page=await Ai(runtime,s=>s.AssistanceHistoryAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,0,1));Assert.Equal(first.Id,Assert.Single(page.Items).Id);Assert.Equal(1,page.NextSkip);
            var next=await Ai(runtime,s=>s.AssistanceHistoryAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,1,1));Assert.Equal(second.Id,Assert.Single(next.Items).Id);Assert.Null(next.NextSkip);
            await Rejected(400,()=>Ai(runtime,s=>s.AssistanceHistoryAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,0,0)));
            e=await AssignTask(f,e,e.Tasks[0].Id);Assert.All((await AiHistory(runtime,f,e)).Items,r=>Assert.False(r.IsCurrent));Assert.Equal(2,fake.Calls);
        });
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task InputBudgetRejectsLongStepsWithoutCallingOrTruncatingModelInput()
    {
        var f=await ExecutionSeed(customize:c=>c with{Steps=c.Steps.Select((s,i)=>i==0?s with{Instructions=new string('a',3500)}:s).ToList()});var e=await BeginExecution(f);var fake=new FakeAssistant();
        await WithAssistant(fake,async runtime=>{await Rejected(409,()=>AiContext(runtime,f,e));Assert.Equal(0,fake.Calls);});
        Assert.Equal(3500,e.Tasks[0].Instructions.Length);e=await AssignTask(f,e,e.Tasks[0].Id);Assert.Equal(3500,e.Tasks[0].Instructions.Length);
    }
    [Fact,Trait("Feature","LocalAi")]
    public async Task LimitedSqlRuntimeCanRecordAndReadAssessmentsWithoutMutatingHistory()
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);var role="openirp_ai_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grants=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grants.ExecuteNonQueryAsync();
            builder.Username=role;builder.Password=password;
            await WithAssistant(new FakeAssistant(),async runtime=>{var row=await Explain(runtime,f,e);Assert.Equal("VALID",row.Outcome);Assert.Equal(row.Id,Assert.Single((await AiHistory(runtime,f,e)).Items).Id);},builder.ConnectionString);
            await using var sql=new NpgsqlConnection(builder.ConnectionString);await sql.OpenAsync();await using var update=new NpgsqlCommand("UPDATE app.assistance_assessments SET model='tampered'",sql);
            Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>update.ExecuteNonQueryAsync())).SqlState);
        }finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }

}
