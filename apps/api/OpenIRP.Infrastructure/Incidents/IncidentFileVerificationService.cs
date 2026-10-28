using System.Text.Json;
using OpenIRP.Application.Incidents;
namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    public Task<EvidenceFileVerification> VerifyFileAsync(Guid actor,Guid organization,Guid incident,Guid evidence,
        VerifyEvidenceFile request,CancellationToken ct=default) =>
        FileGate(actor,organization,incident,evidence,true,true,"evidence.file.verify",async (row,file) =>
        {
            Reason(request.Reason);
            if(request.Reason.Contains('\0')) throw Error(400,"invalid_file_verification_reason");
            if(file.Revision!=request.ExpectedFileRevision) throw Error(409,"stale_evidence_file_revision");
            var verified=false;string? failure=null;
            try { await using var stream=await files.OpenVerifiedAsync(FileDescriptor(file),ct);verified=true; }
            catch(EvidenceStorageException ex) { failure=ex.Code;SetFileState(file,actor,"QUARANTINED",ex.Code,request.Reason); }
            catch(IOException) { throw Error(503,"file_storage_unavailable"); }
            catch(UnauthorizedAccessException) { throw Error(503,"file_storage_unavailable"); }
            var result=new EvidenceFileVerification(evidence,file.Revision,file.State,verified,
                verified && row.Status=="ACTIVE" && file.State=="AVAILABLE",EvidenceNow(),failure);
            db.AuditEvents.Add(audit.Success(new("evidence.file.verification_result",actor,organization,"evidence",evidence),
                JsonSerializer.Serialize(new {result.PhysicalIntegrityVerified,result.DownloadAllowed,result.Revision,result.State,result.FailureCode,reason=request.Reason.Trim()})));
            return result;
        },ct);
}
