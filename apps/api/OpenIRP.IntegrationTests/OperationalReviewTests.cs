using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Xunit;
namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<OperationalReviewPage> Operations(ExecutionFixture f,ExecutionItem e,Guid? actor=null,long after=0,int limit=50) =>
        Executions(s => s.OperationalReviewsAsync(actor??f.Admin,f.Org,f.Incident.Id,e.Id,after,limit));
    private async Task<OperationalReviewItem> ReviewOperation(ExecutionFixture f,ExecutionItem e,Guid evidence,OperationalReviewKind kind=OperationalReviewKind.Evidence,bool accepted=true,RegisterOperationalReview? custom=null)
    {
        var page=await Operations(f,e);var parent=await Incidents(s => s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        return await Executions(s => s.ReviewOperationAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,
            custom??new(kind,evidence,accepted,"Inspeccion humana reproducible","Pertinencia, alcance y limitaciones revisados",
                kind==OperationalReviewKind.Alternative ? "Procedimiento manual alternativo verificado" : null,"Validar soporte",true,"Declaro revision propia",parent.Revision,page.ContextToken)));
    }
    [Theory,Trait("Feature","OperationalReviews")]
    [InlineData(OperationalReviewKind.Evidence)] [InlineData(OperationalReviewKind.Alternative)]
    public async Task OperationalReviewsAreExplicitCurrentAndDoNotAuthorizeTaskOrClosure(OperationalReviewKind kind)
    {
        var (f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);
        e=await Executions(s => s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        if(kind==OperationalReviewKind.Evidence)e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var before=await Operations(f,e);var risk=(await ClosureRead(f.Admin,f.Org,f.Incident.Id)).RiskReview.ContextToken;
        var review=await ReviewOperation(f,e,evidence.Evidence.Id,kind);
        var after=await Operations(f,e);Assert.Equal(before.ContextToken,after.ContextToken);
        var item=Assert.Single(after.Items);Assert.True(item.AcceptanceCurrent);Assert.Equal(review.Id,item.Id);
        Assert.False(after.AuthorizesExecution);Assert.True(after.RequiresHumanReview);
        var unchanged=await Executions(s => s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        Assert.Equal(e.Revision,unchanged.Revision);Assert.Equal(e.Tasks[0].Status,unchanged.Tasks[0].Status);
        Assert.NotEqual(risk,(await ClosureRead(f.Admin,f.Org,f.Incident.Id)).RiskReview.ContextToken);
        var evt=(await Incidents(s => s.EventsAsync(f.Admin,f.Org,f.Incident.Id))).Items.Last();
        Assert.Equal("OPERATIONAL_REVIEWED",evt.Action);Assert.Equal(review.CorrelationId,evt.CorrelationId);
        Assert.DoesNotContain(review.Assessment,evt.Details.GetRawText());
    }
    [Fact,Trait("Feature","OperationalReviews")]
    public async Task OperationalRejectionSupersedesAcceptanceAndHistoryRemainsImmutable()
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var first=await ReviewOperation(f,e,evidence.Evidence.Id);var second=await ReviewOperation(f,e,evidence.Evidence.Id,accepted:false);
        var page=await Operations(f,e);Assert.False(page.Items[0].IsCurrent);Assert.True(page.Items[1].IsCurrent);Assert.False(page.Items[1].AcceptanceCurrent);
        Assert.Equal(second.Id,Assert.Single((await Operations(f,e,after:first.IncidentRevision)).Items).Id);
        await InScope(async(_,db)=>{var row=await db.OperationalReviews.SingleAsync(r=>r.Id==first.Id);row.Assessment="Rewrite";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());return true;});
        await InScope(async(_,db)=>{await Assert.ThrowsAnyAsync<Exception>(()=>db.Database.ExecuteSqlRawAsync("UPDATE app.operational_reviews SET assessment='rewrite'"));return true;});
        await InScope(async(_,db)=>{await Assert.ThrowsAnyAsync<Exception>(()=>db.Database.ExecuteSqlRawAsync("TRUNCATE app.operational_reviews"));return true;});
    }
    [Theory,Trait("Feature","OperationalReviews")]
    [InlineData("withdraw")] [InlineData("assignment")]
    public async Task OperationalReviewContextInvalidatesOnRelevantChanges(string change)
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        await ReviewOperation(f,e,evidence.Evidence.Id);
        if(change=="withdraw")await Evidence(s=>s.WithdrawAsync(f.Admin,f.Org,f.Incident.Id,evidence.Evidence.Id,new(1,"Retirada posterior")));
        else{e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));await AssignTask(f,e,e.Tasks[1].Id);}
        Assert.False(Assert.Single((await Operations(f,e)).Items).IsCurrent);
    }
    [Theory,Trait("Feature","OperationalReviews")]
    [InlineData("analyst")] [InlineData("viewer")] [InlineData("global")]
    public async Task OperationalReviewRequiresLocalAdminEvenForRead(string role)
    {
        var f=await ExecutionSeed();var e=await BeginExecution(f);
        var actor=f.Platform;
        if(role!="global")await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(actor,role=="analyst" ? OrganizationRole.Analyst:OrganizationRole.Viewer)));
        await Rejected(403,()=>Operations(f,e,actor));
    }
    [Theory,Trait("Feature","OperationalReviews")]
    [InlineData("context")] [InlineData("revision")] [InlineData("self")] [InlineData("method")] [InlineData("scope")]
    public async Task OperationalReviewRejectsInvalidInputWithoutBusinessHistory(string error)
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var page=await Operations(f,e);var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        var request=new RegisterOperationalReview(OperationalReviewKind.Evidence,evidence.Evidence.Id,true,"Metodo","Evaluacion",null,"Motivo",true,"Propia",parent.Revision,page.ContextToken);
        request=error switch {"context"=>request with{ExpectedContext="old"},"revision"=>request with{ExpectedIncidentRevision=1},"self"=>request with{SelfReview=false,SelfReviewJustification=null},"method"=>request with{Method=" "},_=>request with{EvidenceId=Guid.NewGuid()}};
        await Rejected(error=="scope"?404:error is "context" or "revision"?409:400,()=>ReviewOperation(f,e,evidence.Evidence.Id,custom:request));
        Assert.Empty((await Operations(f,e)).Items);Assert.Equal(parent.Revision,(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Revision);
    }
    [Fact,Trait("Feature","OperationalReviews")]
    public async Task OperationalReviewsRequireExactCompletionEvidenceAndConcurrentRevisionIsSingleWinner()
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
        await Rejected(409,()=>ReviewOperation(f,e,evidence.Evidence.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var page=await Operations(f,e);var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        var request=new RegisterOperationalReview(OperationalReviewKind.Evidence,evidence.Evidence.Id,true,"Metodo","Evaluacion",null,"Motivo",true,"Propia",parent.Revision,page.ContextToken);
        var attempts=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{try{await ReviewOperation(f,e,evidence.Evidence.Id,custom:request);return true;}catch(OpenIRP.Application.Identity.IdentityOperationException ex){Assert.Equal(409,ex.StatusCode);return false;}}));
        Assert.Single(attempts,x=>x);Assert.Single((await Operations(f,e)).Items);
    }
    [Fact,Trait("Feature","OperationalReviews")]
    public async Task OperationalReviewHttpRequiresExplicitDecisionCsrfAndNoStore()
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var root=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}";
        using var anonymous=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(root+"/operational-reviews")).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        var page=await Operations(f,e);var request=new RegisterOperationalReview(OperationalReviewKind.Evidence,evidence.Evidence.Id,true,"Metodo","Evaluacion",null,"Motivo",true,"Propia",e.IncidentRevision,page.ContextToken);
        var route=root+$"/tasks/{e.Tasks[0].Id}/operational-reviews";
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,new {kind="Evidence",evidenceId=evidence.Evidence.Id,method="Metodo",assessment="Evaluacion",reason="Motivo",selfReview=true,selfReviewJustification="Propia",expectedIncidentRevision=e.IncidentRevision,expectedContext=page.ContextToken})).StatusCode);
        var csrf=admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(route,request)).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync(route,request)).StatusCode);
        using var response=await admin.GetAsync(root+"/operational-reviews");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact,Trait("Feature","OperationalReviews")]
    public async Task LimitedSqlRuntimeCanRecordOperationalReviewButCannotRewriteIt()
    {
        var(f,e)=await EvidenceSetup();var evidence=await AddEvidence(f,e);e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));e=await CompleteWithEvidence(f,e,evidence.Evidence.Id);
        var role="openirp_ops_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grants=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grants.ExecuteNonQueryAsync();
            builder.Username=role;builder.Password=password;
            await using var runtime=new TestApplication(builder.ConnectionString);
            using(var scope=runtime.Services.CreateScope()){
                var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();var page=await service.OperationalReviewsAsync(f.Admin,f.Org,f.Incident.Id,e.Id);
                var review=await service.ReviewOperationAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(OperationalReviewKind.Evidence,evidence.Evidence.Id,true,"Metodo","Evaluacion",null,"Validar",true,"Propia",e.IncidentRevision,page.ContextToken));
                Assert.True(review.AcceptanceCurrent);
            }
            await using var sql=new NpgsqlConnection(builder.ConnectionString);await sql.OpenAsync();
            await using var update=new NpgsqlCommand("UPDATE app.operational_reviews SET assessment='Tampered'",sql);Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>update.ExecuteNonQueryAsync())).SqlState);
        } finally {NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }

}
