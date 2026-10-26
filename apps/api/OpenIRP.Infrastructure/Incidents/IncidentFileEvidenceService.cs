using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private async Task<Guid?> FileRequestScope(Incident parent, RegisterFileEvidence request, CancellationToken ct)
    {
        Reason(request.Reason);
        if(parent.Revision!=request.ExpectedIncidentRevision) throw Error(409,"stale_incident_revision");
        if(parent.Status is "CLOSED" or "DISCARDED") throw Error(409,"terminal_incident_requires_reopening");
        if(!Enum.IsDefined(request.Classification)) throw Error(400,"invalid_evidence_type_or_classification");
        if(request.Classification==EvidenceClassification.Restricted) throw Error(409,"restricted_evidence_policy_pending");
        if(string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>200 || request.Title.Contains('\0')) throw Error(400,"invalid_evidence_content");
        if((request.ExecutionId is null)!=(request.TaskId is null)) throw Error(400,"incomplete_evidence_task_scope");
        if(request.TaskId is not Guid task) return null;
        var item=await db.ExecutionTasks.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==task && t.ExecutionId==request.ExecutionId
            && t.OrganizationId==parent.OrganizationId && t.IncidentId==parent.Id,ct) ?? throw Error(404,"execution_task_not_found");
        if(parent.Status!="IN_RESPONSE" || item.Status=="COMPLETED" ||
            !await db.PlaybookExecutions.AnyAsync(e=>e.Id==item.ExecutionId && e.Status=="IN_PROGRESS",ct)) throw Error(409,"evidence_task_not_active");
        return item.VersionId;
    }
    private static StagedEvidenceFile FileDescriptor(EvidenceFile f)=>new(f.StorageId,f.OriginalName,f.ContentType,f.Length,f.Sha256);
    private static EvidenceFileItem FileView(IncidentEvidence e,EvidenceFile f)=>new(e.Id,f.State,f.Revision,
        e.Status=="ACTIVE" ? f.OriginalName:null,e.Status=="ACTIVE" ? f.ContentType:null,f.Length,
        e.Status=="ACTIVE" ? f.Sha256:null,f.FailureCode,f.ChangedAt);
    private void FileEvent(EvidenceFile f,Guid actor,string reason)=>db.EvidenceFileEvents.Add(new()
    {
        OrganizationId=f.OrganizationId,IncidentId=f.IncidentId,EvidenceId=f.EvidenceId,Revision=f.Revision,State=f.State,
        FailureCode=f.FailureCode,ActorUserId=actor,CorrelationId=context.CorrelationId,Reason=reason.Trim(),OccurredAt=EvidenceNow()
    });
    private async Task<EvidenceFile> FindFile(Guid organization,Guid incident,Guid evidence,CancellationToken ct)=>
        await db.EvidenceFiles.SingleOrDefaultAsync(f=>f.OrganizationId==organization && f.IncidentId==incident && f.EvidenceId==evidence,ct)
            ?? throw Error(404,"evidence_file_not_found");
    private async Task<T> FileGate<T>(Guid actor,Guid organization,Guid incident,Guid evidence,bool write,bool admin,string action,
        Func<IncidentEvidence,EvidenceFile,Task<T>> operation,CancellationToken ct)
    {
        try
        {
            if(db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Use a clean file scope.");
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'",ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1869636978, 26)",ct);
            await AuthorizeAsync(actor,organization,write,ct);
            if(admin) await FileAdmin(actor,organization,ct);
            await FindAsync(organization,incident,ct);
            var row=await FindEvidence(organization,incident,evidence,ct);
            var file=await FindFile(organization,incident,evidence,ct);
            var result=await operation(row,file);
            db.AuditEvents.Add(audit.Success(new(action,actor,organization,"evidence",evidence),
                JsonSerializer.Serialize(new{file.Revision,file.State})));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
        }
        catch(Exception ex)
        {
            await audit.RejectedAsync(new(action,actor,organization,"evidence",evidence),ex);
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }
    private async Task FileAdmin(Guid actor,Guid organization,CancellationToken ct)
    {
        if(!await db.OrganizationMemberships.AnyAsync(m=>m.OrganizationId==organization && m.UserId==actor
            && m.Status==MembershipStatus.Active && m.Role==OrganizationRole.OrgAdmin,ct)) throw Error(403,"organization_admin_required");
    }
    private void SetFileState(EvidenceFile file,Guid actor,string state,string? code,string reason)
    {
        if(file.State==state && file.FailureCode==code) return;
        if(file.Revision==long.MaxValue) throw Error(409,"file_revision_exhausted");
        file.State=state; file.FailureCode=code; file.Revision++; file.ChangedAt=EvidenceNow(); FileEvent(file,actor,reason);
    }
    private Task<EvidenceFileItem> FinishFile(Guid actor,Guid organization,Guid incident,Guid evidence,long expected,string state,string? code,
        string reason,bool admin,CancellationToken ct,bool integrityRead=false)=>
        FileGate(actor,organization,incident,evidence,!integrityRead,admin,integrityRead ? "evidence.file.integrity":"evidence.file.finalize",
            (row,file)=>
        {
            if(row.Status!="ACTIVE") {state="QUARANTINED";code="withdrawn";}
            if(file.State==state && file.FailureCode==code) return Task.FromResult(FileView(row,file));
            if(file.Revision!=expected) throw Error(409,"stale_evidence_file_revision");
            if(file.State=="QUARANTINED") throw Error(409,"evidence_file_quarantined");
            if(file.State=="AVAILABLE" && state=="FAILED") state="QUARANTINED";
            SetFileState(file,actor,state,code,reason);
            return Task.FromResult(FileView(row,file));
        },ct);

    public async Task<FileEvidenceResult> UploadAsync(Guid actor,Guid organization,Guid incident,RegisterFileEvidence request,
        Stream source,string name,string contentType,CancellationToken ct=default)
    {
        await EvidenceRead(actor,organization,incident,async()=>
        {
            await AuthorizeAsync(actor,organization,true,ct);
            return await FileRequestScope(await FindAsync(organization,incident,ct),request,ct);
        },ct);
        StagedEvidenceFile stage;
        try {stage=await files.StageAsync(source,name,contentType,ct);}
        catch(IdentityOperationException ex){await audit.RejectedAsync(new("evidence.file.upload",actor,organization,"incident",incident),ex);throw;}
        catch(EvidenceStorageException){throw Error(503,"file_storage_unavailable");}
        catch(IOException){throw Error(503,"file_storage_unavailable");}
        catch(UnauthorizedAccessException){throw Error(503,"file_storage_unavailable");}
        IncidentEvidence? row=null;
        try
        {
            await WriteAsync(actor,organization,incident,"EVIDENCE_REGISTERED",async()=>
            {
                var parent=await FindAsync(organization,incident,ct);
                var version=await FileRequestScope(parent,request,ct); Next(parent,request.ExpectedIncidentRevision);
                row=new(){OrganizationId=organization,IncidentId=incident,ExecutionId=request.ExecutionId,TaskId=request.TaskId,VersionId=version,
                    Kind="FILE",Classification=request.Classification.ToString().ToUpperInvariant(),Title=request.Title.Trim(),Content="[file]",
                    CreatedByUserId=actor,CreatedAt=EvidenceNow()};
                db.IncidentEvidence.Add(row); EvidenceEvent(row,actor,"REGISTERED",request.Reason);
                var file=new EvidenceFile{EvidenceId=row.Id,OrganizationId=organization,IncidentId=incident,StorageId=stage.StorageId,
                    OriginalName=stage.OriginalName,ContentType=stage.ContentType,Length=stage.Length,Sha256=stage.Sha256,
                    CreatedAt=row.CreatedAt,ChangedAt=row.CreatedAt};
                db.EvidenceFiles.Add(file);FileEvent(file,actor,request.Reason);
                return(parent,request.Reason.Trim());
            },ct,()=>JsonSerializer.Serialize(new{evidenceId=row!.Id,kind="FILE",row.TaskId}));
        }
        catch(Exception failure)
        {
            // Absence in another connection does not prove that an in-flight commit cannot succeed.
            // Only a definite rejection/rollback permits deleting this upload's unregistered staging.
            var server=failure as PostgresException ?? (failure as DbUpdateException)?.InnerException as PostgresException;
            var rolledBack=failure is IdentityOperationException || server is not null &&
                (server.SqlState.StartsWith("23",StringComparison.Ordinal) || server.SqlState=="P0001");
            if(rolledBack)
            {
                bool? exists=null;
                try{using var lookup=new CancellationTokenSource(TimeSpan.FromSeconds(3));exists=await db.EvidenceFiles.AsNoTracking().AnyAsync(f=>f.StorageId==stage.StorageId,lookup.Token);}catch{ }
                if(exists==false) await files.DiscardUnregisteredAsync(stage.StorageId);
            }
            throw;
        }
        string state="AVAILABLE";string? code=null;
        try{await files.PublishAsync(stage,ct);}
        catch(EvidenceStorageException ex){state=ex.Code=="integrity_check_failed" || ex.Code=="unsafe_storage_path" ? "QUARANTINED":"FAILED";code=ex.Code;}
        catch(IOException){state="FAILED";code="storage_commit_failed";}
        catch(UnauthorizedAccessException){state="FAILED";code="storage_commit_failed";}
        await FinishFile(actor,organization,incident,row!.Id,1,state,code,"Confirmar persistencia del archivo",false,ct);
        return await EvidenceRead(actor,organization,incident,async()=>
        {
            var current=await FindEvidence(organization,incident,row.Id,ct);
            var parent=await FindAsync(organization,incident,ct);
            return new FileEvidenceResult(new(EvidenceView(current),parent.Revision),FileView(current,await FindFile(organization,incident,row.Id,ct)));
        },ct);
    }

    public Task<EvidenceFileItem> FileMetadataAsync(Guid actor,Guid organization,Guid incident,Guid evidence,CancellationToken ct=default)=>
        EvidenceRead(actor,organization,incident,async()=>FileView(await FindEvidence(organization,incident,evidence,ct),await FindFile(organization,incident,evidence,ct)),ct);
    public Task<IReadOnlyList<EvidenceFileEventItem>> FileEventsAsync(Guid actor,Guid organization,Guid incident,Guid evidence,long afterRevision=0,int limit=50,CancellationToken ct=default)=>
        EvidenceRead<IReadOnlyList<EvidenceFileEventItem>>(actor,organization,incident,async()=>
        {
            if(afterRevision<0 || limit is <1 or >100) throw Error(400,"invalid_evidence_pagination");
            await FindFile(organization,incident,evidence,ct);
            return await db.EvidenceFileEvents.AsNoTracking().Where(f=>f.OrganizationId==organization && f.IncidentId==incident
                && f.EvidenceId==evidence && f.Revision>afterRevision).OrderBy(f=>f.Revision).Take(limit)
                .Select(f=>new EvidenceFileEventItem(f.Revision,f.State,f.FailureCode,f.ActorUserId,f.CorrelationId,f.Reason,f.OccurredAt)).ToListAsync(ct);
        },ct);
    public async Task<EvidenceFileItem> RecoverFileAsync(Guid actor,Guid organization,Guid incident,Guid evidence,RecoverEvidenceFile request,CancellationToken ct=default)
    {
        var file=await EvidenceRead(actor,organization,incident,async()=>
        {
            await AuthorizeAsync(actor,organization,true,ct);await FileAdmin(actor,organization,ct);Reason(request.Reason);
            var row=await FindEvidence(organization,incident,evidence,ct);var current=await FindFile(organization,incident,evidence,ct);
            if(row.Status!="ACTIVE") throw Error(409,"evidence_already_withdrawn");
            if(current.State=="QUARANTINED") throw Error(409,"evidence_file_quarantined");
            if(current.Revision!=request.ExpectedFileRevision) throw Error(409,"stale_evidence_file_revision");
            return FileDescriptor(current);
        },ct);
        string state="AVAILABLE";string? code=null;
        try{await files.PublishAsync(file,ct);}
        catch(EvidenceStorageException ex){state=ex.Code is "integrity_check_failed" or "unsafe_storage_path" ? "QUARANTINED":"FAILED";code=ex.Code;}
        catch(IOException){state="FAILED";code="storage_commit_failed";}
        catch(UnauthorizedAccessException){state="FAILED";code="storage_commit_failed";}
        return await FinishFile(actor,organization,incident,evidence,request.ExpectedFileRevision,state,code,request.Reason,true,ct);
    }

    public async Task<EvidenceDownload> DownloadAsync(Guid actor,Guid organization,Guid incident,Guid evidence,CancellationToken ct=default)
    {
        var snapshot=await EvidenceRead(actor,organization,incident,async()=>
        {
            var row=await FindEvidence(organization,incident,evidence,ct);var file=await FindFile(organization,incident,evidence,ct);
            if(row.Status!="ACTIVE" || file.State!="AVAILABLE") throw Error(409,"evidence_file_not_available");
            return (File:FileDescriptor(file),file.Revision);
        },ct);
        Stream stream;
        try{stream=await files.OpenVerifiedAsync(snapshot.File,ct);}
        catch(EvidenceStorageException ex)
        {
            await FinishFile(actor,organization,incident,evidence,snapshot.Revision,"QUARANTINED",ex.Code,"Bloquear archivo no verificable",false,ct,true);
            throw Error(409,"evidence_file_not_available");
        }
        catch(IOException){throw Error(503,"file_storage_unavailable");}
        catch(UnauthorizedAccessException){throw Error(503,"file_storage_unavailable");}
        try
        {
            await FileGate(actor,organization,incident,evidence,false,false,"evidence.file.download",(row,file)=>
            {
                if(row.Status!="ACTIVE" || file.State!="AVAILABLE" || file.Revision!=snapshot.Revision) throw Error(409,"evidence_file_not_available");
                return Task.FromResult(true);
            },ct);
            return new(stream,snapshot.File.ContentType,snapshot.File.OriginalName,snapshot.File.Length);
        }
        catch{await stream.DisposeAsync();throw;}
    }
    private async Task QuarantineWithdrawnFile(IncidentEvidence row,Guid actor,string reason,CancellationToken ct)
    {
        if(row.Kind!="FILE") return;
        var file=await FindFile(row.OrganizationId,row.IncidentId,row.Id,ct);
        if(file.State!="QUARANTINED") SetFileState(file,actor,"QUARANTINED","withdrawn",reason);
    }
    private async Task VerifyCompletionFile(Guid organization,Guid incident,Guid evidence,CancellationToken ct)
    {
        var row=await FindEvidence(organization,incident,evidence,ct);
        if(row.Kind!="FILE") return;
        var file=await FindFile(organization,incident,evidence,ct);
        if(file.State!="AVAILABLE") throw Error(409,"evidence_file_not_available");
        try{await using var content=await files.OpenVerifiedAsync(FileDescriptor(file),ct);}
        catch(EvidenceStorageException){throw Error(409,"evidence_file_not_available");}
        catch(IOException){throw Error(409,"evidence_file_not_available");}
        catch(UnauthorizedAccessException){throw Error(409,"evidence_file_not_available");}
    }}
