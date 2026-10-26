using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Incidents;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private sealed class FileStoreFault(IPrivateEvidenceStore inner,string? failure):IPrivateEvidenceStore
    {
        private int publications;
        public int Opens {get;private set;}
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<StagedEvidenceFile> StageAsync(Stream input,string name,string type,CancellationToken ct)=>inner.StageAsync(input,name,type,ct);
        public async Task PublishAsync(StagedEvidenceFile file,CancellationToken ct)
        { if(failure=="hold_publish"){Entered.TrySetResult();await Release.Task.WaitAsync(ct);} if(failure=="publish_once" && Interlocked.Increment(ref publications)==1)throw new IOException("Injected persistence failure");await inner.PublishAsync(file,ct); }
        public Task<Stream> OpenVerifiedAsync(StagedEvidenceFile file,CancellationToken ct){Opens++;return inner.OpenVerifiedAsync(file,ct);}
        public Task DiscardUnregisteredAsync(Guid id)=>inner.DiscardUnregisteredAsync(id);
    }
    private async Task WithFiles(Func<WebApplicationFactory<Program>,string,Task> action,string? failure=null,string? runtimeConnection=null,Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor=null)
    {
        var root=Path.Combine(Path.GetTempPath(),"openirp-file-test-"+Guid.NewGuid().ToString("N"));
        var connection=await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString()));
        try
        {
            await using var configured=new TestApplication(runtimeConnection??connection!,root);
            await using var runtime=configured.WithWebHostBuilder(b=>b.ConfigureServices(s=>
            {
                if(interceptor is not null)s.AddDbContext<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>(o=>o.AddInterceptors(interceptor));
                s.RemoveAll<IPrivateEvidenceStore>();
                s.AddSingleton<IPrivateEvidenceStore>(p=>new FileStoreFault(new PrivateEvidenceStore(p.GetRequiredService<IConfiguration>()),failure));
            }));
            await action(runtime,root);
        }
        finally
        {
            var resolved=Path.GetFullPath(root);
            var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("openirp-file-test-",StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside owned test root.");
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
    }
    private static async Task<T> Files<T>(WebApplicationFactory<Program> runtime,Func<IFileEvidenceService,Task<T>> action)
    { using var scope=runtime.Services.CreateScope();return await action(scope.ServiceProvider.GetRequiredService<IFileEvidenceService>()); }
    private async Task<FileEvidenceResult> UploadFile(WebApplicationFactory<Program> runtime,ExecutionFixture f,ExecutionItem? e=null,
        byte[]? body=null,string name="evidence.txt",string type="text/plain",Guid? actor=null)
    {
        var parent=await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id));
        await using var content=new MemoryStream(body??Encoding.UTF8.GetBytes("Contenido de evidencia privada."));
        return await Files(runtime,s=>s.UploadAsync(actor??f.Admin,f.Org,f.Incident.Id,new(EvidenceClassification.Confidential,
            "Archivo operativo",e?.Id,e?.Tasks[0].Id,parent.Revision,"Registrar archivo de soporte"),content,name,type));
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData("txt","text/plain")]
    [InlineData("pdf","application/pdf")]
    [InlineData("png","image/png")]
    [InlineData("jpg","image/jpeg")]
    [InlineData("jpeg","image/jpeg")]
    public async Task FileUploadDownloadAndHashSupportApprovedFormats(string extension,string mime)
    {
        var f=await ExecutionSeed();
        var body=extension switch{"pdf"=>Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"),"png"=>new byte[]{137,80,78,71,13,10,26,10,0},"jpg" or "jpeg"=>new byte[]{255,216,255,224,255,217},_=>Encoding.UTF8.GetBytes("Prueba UTF-8 ñ")};
        await WithFiles(async(runtime,root)=>
        {
            var result=await UploadFile(runtime,f,body:body,name:"evidence."+extension,type:mime);
            Assert.Equal("AVAILABLE",result.File.State);Assert.Equal(2,result.File.Revision);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(body)),result.File.Sha256);
            Assert.Null(result.Registration.Evidence.Content);
            var download=await Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,result.File.EvidenceId));
            await using(download.Content){using var copy=new MemoryStream();await download.Content.CopyToAsync(copy);Assert.Equal(body,copy.ToArray());}
            Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
            Assert.Empty(Directory.GetFiles(Path.Combine(root,"staging")));
            var history=await Files(runtime,s=>s.FileEventsAsync(f.Admin,f.Org,f.Incident.Id,result.File.EvidenceId));
            Assert.Equal(new[]{"PENDING","AVAILABLE"},history.Select(h=>h.State));
        });
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData("extension",415)]
    [InlineData("mime",415)]
    [InlineData("signature",415)]
    [InlineData("path",400)]
    [InlineData("empty",400)]
    [InlineData("utf8",415)]
    [InlineData("oversize",413)]
    public async Task FileValidationRejectsBeforeRegisteringMetadata(string invalid,int status)
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var body=invalid switch{"empty"=>Array.Empty<byte>(),"utf8"=>new byte[]{255,0,255},"oversize"=>new byte[10*1024*1024+1],_=>Encoding.UTF8.GetBytes("Contenido")};
            var name=invalid switch{"extension"=>"payload.exe","signature"=>"fake.png","path"=>"../outside.txt",_=>"evidence.txt"};
            var type=invalid=="signature" ? "image/png":invalid=="mime" ? "application/pdf":"text/plain";
            await Rejected(status,()=>UploadFile(runtime,f,body:body,name:name,type:type));
            Assert.Equal(0,await InScope((_,db)=>db.EvidenceFiles.CountAsync()));
            if(Directory.Exists(Path.Combine(root,"staging")))Assert.Empty(Directory.GetFiles(Path.Combine(root,"staging")));
        });
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileSizeLimitIncludesExactlyTenMegabytes()
    {
        var f=await ExecutionSeed();var body=Enumerable.Repeat((byte)'x',10*1024*1024).ToArray();
        await WithFiles(async(runtime,_)=>Assert.Equal(body.Length,(await UploadFile(runtime,f,body:body)).File.Length));
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileRecoveryPublishesFailedStagingWithoutChangingIncidentRevision()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var row=await UploadFile(runtime,f);
            Assert.Equal("FAILED",row.File.State);Assert.Single(Directory.GetFiles(Path.Combine(root,"staging")));
            var recovered=await Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(2,"Reintentar persistencia")));
            Assert.Equal("AVAILABLE",recovered.State);Assert.Equal(3,recovered.Revision);
            Assert.Equal(row.Registration.IncidentRevision,(await Incidents(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id))).Revision);
            Assert.Equal(recovered,await Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(3,"Verificar de nuevo"))));
            Assert.Empty(Directory.GetFiles(Path.Combine(root,"staging")));
        },"publish_once");
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData("registration")]
    [InlineData("finalization")]
    public async Task DatabaseFailureCleansOnlyUnregisteredStagingAndRecoversCommittedFiles(string point)
    {
        var f=await ExecutionSeed();
        var table=point=="registration" ? "app.evidence_events":"app.evidence_files";
        var operation=point=="registration" ? "INSERT":"UPDATE";
        var script="CREATE FUNCTION app.fail_file_test() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RAISE EXCEPTION 'Injected file failure'; END; $body$; CREATE TRIGGER fail_file_test BEFORE "+operation+" ON "+table+" FOR EACH ROW EXECUTE FUNCTION app.fail_file_test();";
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(script));
        await WithFiles(async(runtime,root)=>
        {
            await Assert.ThrowsAsync<DbUpdateException>(()=>UploadFile(runtime,f));
            if(point=="registration")
            {
                Assert.Equal(0,await InScope((_,db)=>db.EvidenceFiles.CountAsync()));
                Assert.Empty(Directory.GetFiles(Path.Combine(root,"staging")));Assert.Empty(Directory.GetFiles(Path.Combine(root,"objects")));
            }
            else
            {
                var row=await InScope((_,db)=>db.EvidenceFiles.AsNoTracking().SingleAsync());Assert.Equal("PENDING",row.State);
                Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
                var drop="DROP TRIGGER fail_file_test ON "+table+"; DROP FUNCTION app.fail_file_test();";
                await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(drop));
                var result=await Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.EvidenceId,new(1,"Recuperar confirmación")));
                Assert.Equal("AVAILABLE",result.State);
            }
        });
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawnFilesRemainPrivateAndPhysicallyRetained(bool afterCompletion)
    {
        var (f,e)=await EvidenceSetup();
        await WithFiles(async(runtime,root)=>
        {
            var row=await UploadFile(runtime,f,e);
            if(afterCompletion)
            {
                e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
                using var scope=runtime.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();
                e=await service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"Completar con archivo",row.File.EvidenceId));
            }
            using(var scope=runtime.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IEvidenceService>().WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(1,"Retirar acceso"));
            var meta=await Files(runtime,s=>s.FileMetadataAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId));
            Assert.Equal("QUARANTINED",meta.State);Assert.Null(meta.OriginalName);Assert.Null(meta.Sha256);
            await Rejected(409,()=>Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId)));
            Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
            if(afterCompletion)Assert.Equal(row.File.EvidenceId,(await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id))).Tasks[0].CompletionEvidenceId);
        });
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData("tampered")]
    [InlineData("missing")]
    public async Task FileIntegrityFailureBlocksDownloadAndQuarantinesEvenForViewer(string failure)
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var row=await UploadFile(runtime,f);await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,OrganizationRole.Viewer)));
            var path=Directory.GetFiles(Path.Combine(root,"objects")).Single();
            if(failure=="missing")File.Delete(path);else await File.WriteAllTextAsync(path,"Manipulado");
            await Rejected(409,()=>Files(runtime,s=>s.DownloadAsync(f.Platform,f.Org,f.Incident.Id,row.File.EvidenceId)));
            Assert.Equal("QUARANTINED",(await Files(runtime,s=>s.FileMetadataAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId))).State);
            await Rejected(409,()=>Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(3,"No publicar cuarentena"))));
        });
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task PendingFilesCannotSatisfyCompletionAndPhysicalCorruptionIsRechecked()
    {
        var (f,e)=await EvidenceSetup();
        await WithFiles(async(runtime,root)=>
        {
            var row=await UploadFile(runtime,f,e);Assert.Equal("FAILED",row.File.State);
            e=await Executions(s=>s.GetAsync(f.Admin,f.Org,f.Incident.Id,e.Id));
            using var scope=runtime.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<IExecutionService>();
            await Rejected(409,()=>service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"No usar fallo",row.File.EvidenceId)));
            await Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(2,"Recuperar")));
            await File.WriteAllTextAsync(Directory.GetFiles(Path.Combine(root,"objects")).Single(),"Alterado");
            await Rejected(409,()=>service.ChangeAsync(f.Admin,f.Org,f.Incident.Id,e.Id,e.Tasks[0].Id,new(ExecutionTaskState.Completed,e.Revision,e.IncidentRevision,"No usar corrupción",row.File.EvidenceId)));
        },"publish_once");
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData(OrganizationRole.Analyst)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task FilePermissionsRequireCurrentTenantRolesAndAdminRecovery(OrganizationRole role)
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var row=await UploadFile(runtime,f);
            var store=(FileStoreFault)runtime.Services.GetRequiredService<IPrivateEvidenceStore>();var before=store.Opens;
            await Rejected(403,()=>Files(runtime,s=>s.DownloadAsync(f.Platform,f.Org,f.Incident.Id,row.File.EvidenceId)));
            Assert.Equal(before,store.Opens);
            await Act(s=>s.AddMemberAsync(f.Admin,f.Org,new(f.Platform,role)));
            var download=await Files(runtime,s=>s.DownloadAsync(f.Platform,f.Org,f.Incident.Id,row.File.EvidenceId));await download.Content.DisposeAsync();
            await Rejected(403,()=>Files(runtime,s=>s.RecoverFileAsync(f.Platform,f.Org,f.Incident.Id,row.File.EvidenceId,new(2,"No administra"))));
            if(role==OrganizationRole.Viewer)await Rejected(403,()=>UploadFile(runtime,f,actor:f.Platform));
            else Assert.Equal(f.Platform,(await UploadFile(runtime,f,actor:f.Platform)).Registration.Evidence.CreatedByUserId);
        });
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileScopeAndInactiveAccountsCannotReadDiskOrMetadata()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,_)=>
        {
            var row=await UploadFile(runtime,f);var other=await RegisterIncident(f.Admin,f.Org);
            await Rejected(404,()=>Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,other.Id,row.File.EvidenceId)));
            await InScope(async(_,db)=>{(await db.Users.SingleAsync(u=>u.Id==f.Admin)).IsActive=false;return await db.SaveChangesAsync();});
            await Rejected(403,()=>Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId)));
        });
    }
    [Theory,Trait("Feature","EvidenceFiles")]
    [InlineData("metadata")]
    [InlineData("events")]
    [InlineData("delete")]
    public async Task FileSqlProtectsIdentityAndHistories(string invalid)
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,_)=>
        {
            await UploadFile(runtime,f);
            var query=invalid switch{"metadata"=>"UPDATE app.evidence_files SET sha256=repeat('0',64)","events"=>"DELETE FROM app.evidence_file_events",_=>"DELETE FROM app.evidence_files"};
            var ex=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(query)));
            Assert.Equal(invalid=="metadata" ? "23514":"42501",ex.SqlState);
        });
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileMigrationRoundTripPreservesTextEvidenceAndGuardsFileHistory()
    {
        var f=await ExecutionSeed();var text=await AddEvidence(f);
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026163753_IncidentTextReferenceEvidence");await db.Database.MigrateAsync();return true;});
        Assert.Equal(text.Evidence,await Evidence(s=>s.GetEvidenceAsync(f.Admin,f.Org,f.Incident.Id,text.Evidence.Id)));
        await WithFiles(async(runtime,_)=>{await UploadFile(runtime,f);});
        await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261026163753_IncidentTextReferenceEvidence");return true;}));
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileHttpUploadRequiresCsrfAndDownloadIsAnAttachmentWithoutPrivateKeys()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            using var client=runtime.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false,HandleCookies=true});
            var route=$"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/evidence";
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(route+"/"+Guid.NewGuid()+"/file/download")).StatusCode);
            await Login(client,"alpha");
            using var form=new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(new RegisterFileEvidence(EvidenceClassification.Internal,"Archivo HTTP",null,null,3,"Carga HTTP"))),"metadata");
            var body=new ByteArrayContent(Encoding.UTF8.GetBytes("Contenido HTTP"));body.Headers.ContentType=new MediaTypeHeaderValue("text/plain");form.Add(body,"file","evidence.txt");
            var response=await client.PostAsync(route+"/files",form);Assert.Equal(HttpStatusCode.Created,response.StatusCode);
            var result=(await response.Content.ReadFromJsonAsync<FileEvidenceResult>())!;
            var download=await client.GetAsync(route+$"/{result.File.EvidenceId}/file/download");
            Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal("attachment",download.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("nosniff",download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.DoesNotContain(root,await response.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(route+$"/{result.File.EvidenceId}/file/recovery",new RecoverEvidenceFile(2,"Sin CSRF"))).StatusCode);
        });
    }

    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task FileWithdrawalDuringPublicationNeverMakesTheFileDownloadable()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var store=(FileStoreFault)runtime.Services.GetRequiredService<IPrivateEvidenceStore>();
            var upload=UploadFile(runtime,f);
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                var row=await InScope((_,db)=>db.EvidenceFiles.AsNoTracking().SingleAsync());
                using var scope=runtime.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEvidenceService>().WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.EvidenceId,new(1,"Retirar durante carga"));
            }
            finally{store.Release.TrySetResult();}
            var result=await upload;
            Assert.Equal("WITHDRAWN",result.Registration.Evidence.Status);Assert.Equal("QUARANTINED",result.File.State);Assert.Null(result.File.OriginalName);
            await Rejected(409,()=>Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,result.File.EvidenceId)));
            Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));
        },"hold_publish");
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task ConcurrentUploadsWithTheSameIncidentTokenKeepOnlyOneRegisteredObject()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            var request=new RegisterFileEvidence(EvidenceClassification.Internal,"Carga",null,null,3,"Carga concurrente");
            async Task<int> Attempt()
            {
                await using var stream=new MemoryStream(Encoding.UTF8.GetBytes("Contenido"));
                try{await Files(runtime,s=>s.UploadAsync(f.Admin,f.Org,f.Incident.Id,request,stream,"evidence.txt","text/plain"));return 201;}
                catch(IdentityOperationException ex){return ex.StatusCode;}
            }
            Assert.Equal(new[]{201,409},(await Race(Attempt,Attempt)).Order().ToArray());
            Assert.Equal(1,await InScope((_,db)=>db.EvidenceFiles.CountAsync()));
            Assert.Single(Directory.GetFiles(Path.Combine(root,"objects")));Assert.Empty(Directory.GetFiles(Path.Combine(root,"staging")));
        });
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task UploadFailsClosedWhenPrivateRootIsNotConfigured()
    {
        var f=await ExecutionSeed();
        await using var stream=new MemoryStream(Encoding.UTF8.GetBytes("Contenido"));
        using var scope=app.Services.CreateScope();
        await Rejected(503,()=>scope.ServiceProvider.GetRequiredService<IFileEvidenceService>().UploadAsync(f.Admin,f.Org,f.Incident.Id,
            new(EvidenceClassification.Internal,"Archivo",null,null,3,"Sin raíz"),stream,"evidence.txt","text/plain"),"file_storage_not_configured");
        Assert.Equal(0,await InScope((_,db)=>db.EvidenceFiles.CountAsync()));
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task SqlCompletionCannotConsumeAFileWhoseMetadataIsNotAvailable()
    {
        var (f,e)=await EvidenceSetup();
        await WithFiles(async(runtime,_)=>
        {
            var row=await UploadFile(runtime,f,e);
            var query="UPDATE app.playbook_executions SET revision=revision+1; UPDATE app.execution_tasks SET status='COMPLETED',completion_evidence_id={0},changed_at_revision=changed_at_revision+1 WHERE id={1}";
            var ex=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(query,row.File.EvidenceId,e.Tasks[0].Id)));
            Assert.Equal("23514",ex.SqlState);Assert.Contains("Completion requires active evidence",ex.MessageText);
        },"publish_once");
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task SqlFileEvidenceCannotExistWithoutItsFileRecord()
    {
        var f=await ExecutionSeed();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>
        {
            var row=new IncidentEvidence{OrganizationId=f.Org,IncidentId=f.Incident.Id,Kind="FILE",Title="Sin archivo",Content="[file]",CreatedByUserId=f.Admin};
            db.IncidentEvidence.Add(row);
            db.EvidenceEvents.Add(new(){OrganizationId=f.Org,IncidentId=f.Incident.Id,EvidenceId=row.Id,Revision=1,Action="REGISTERED",ActorUserId=f.Admin,CorrelationId=Guid.NewGuid(),Reason="Sin metadata"});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",error.SqlState);
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task RestrictedSqlRuntimeCanUploadDownloadAndWithdrawWithoutMutatingFileIdentity()
    {
        var f=await ExecutionSeed();var role="openirp_file_runtime_"+Guid.NewGuid().ToString("N");var password="Runtime!9"+Guid.NewGuid().ToString("N");
        var builder=new NpgsqlConnectionStringBuilder(await InScope((_,db)=>Task.FromResult(db.Database.GetConnectionString())));
        await using var owner=new NpgsqlConnection(builder.ConnectionString);await owner.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'",owner))await create.ExecuteNonQueryAsync();
        try
        {
            await using(var setting=new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)",owner)){setting.Parameters.AddWithValue("role",role);await setting.ExecuteNonQueryAsync();}
            await using(var grant=new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"ci","grant-runtime.sql")),owner))await grant.ExecuteNonQueryAsync();
            builder.Username=role;builder.Password=password;
            await WithFiles(async(runtime,_)=>
            {
                var row=await UploadFile(runtime,f);
                var downloaded=await Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId));await downloaded.Content.DisposeAsync();
                using var scope=runtime.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEvidenceService>().WithdrawAsync(f.Admin,f.Org,f.Incident.Id,row.File.EvidenceId,new(1,"Retirada runtime"));
                var db=scope.ServiceProvider.GetRequiredService<OpenIRP.Infrastructure.Persistence.OpenIrpDbContext>();
                foreach(var query in new[]{"UPDATE app.evidence_files SET original_name='Alterado'","DELETE FROM app.evidence_files","DELETE FROM app.evidence_file_events"})
                    Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(query))).SqlState);
            },runtimeConnection:builder.ConnectionString);
        }
        finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",owner);await cleanup.ExecuteNonQueryAsync();}
    }

    private sealed class LostCommitAcknowledgement:Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private int fired;
        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,CancellationToken ct=default)
        {
            if(eventData.Context is not null && eventData.Context.ChangeTracker.Entries<EvidenceFile>().Any(e=>e.Entity.State=="PENDING" && e.Entity.Revision==1)
                && Interlocked.Exchange(ref fired,1)==0) throw new IOException("Injected lost commit acknowledgement");
            return Task.CompletedTask;
        }
    }
    [Fact,Trait("Feature","EvidenceFiles")]
    public async Task AmbiguousCommittedRegistrationPreservesStagingForAdministrativeRecovery()
    {
        var f=await ExecutionSeed();
        await WithFiles(async(runtime,root)=>
        {
            await Assert.ThrowsAsync<IOException>(()=>UploadFile(runtime,f));
            var row=await InScope((_,db)=>db.EvidenceFiles.AsNoTracking().SingleAsync());
            Assert.Equal("PENDING",row.State);Assert.Single(Directory.GetFiles(Path.Combine(root,"staging")));
            var ready=await Files(runtime,s=>s.RecoverFileAsync(f.Admin,f.Org,f.Incident.Id,row.EvidenceId,new(1,"Recuperar confirmación perdida")));
            Assert.Equal("AVAILABLE",ready.State);
            var downloaded=await Files(runtime,s=>s.DownloadAsync(f.Admin,f.Org,f.Incident.Id,row.EvidenceId));
            await downloaded.Content.DisposeAsync();
        },interceptor:new LostCommitAcknowledgement());
    }
}
