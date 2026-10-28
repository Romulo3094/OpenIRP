using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Api.Controllers;

[Authorize,ApiController,Route("api/organizations/{organization:guid}/incidents/{incident:guid}/evidence")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class EvidenceFilesController(IFileEvidenceService service):ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("files")]
    [RequestSizeLimit(10*1024*1024+65536)]
    [RequestFormLimits(MultipartBodyLengthLimit=10*1024*1024+65536)]
    public async Task<IActionResult> Upload(Guid organization,Guid incident,[FromForm]string metadata,[FromForm]IFormFile file,CancellationToken ct)
    {
        if(metadata.Length>16384) return BadRequest(new{code="invalid_file_metadata"});
        RegisterFileEvidence? request;
        try{request=JsonSerializer.Deserialize<RegisterFileEvidence>(metadata,new JsonSerializerOptions(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter()}});}
        catch(JsonException){return BadRequest(new{code="invalid_file_metadata"});}
        if(request is null) return BadRequest(new{code="invalid_file_metadata"});
        if(file.Length>10*1024*1024) return StatusCode(413,new{code="evidence_file_too_large"});
        await using var stream=file.OpenReadStream();
        var result=await service.UploadAsync(Actor,organization,incident,request,stream,file.FileName,file.ContentType,ct);
        var location=$"/api/organizations/{organization}/incidents/{incident}/evidence/{result.Registration.Evidence.Id}/file";
        return result.File.State=="AVAILABLE" ? Created(location,result):Accepted(location,result);
    }
    [HttpGet("{evidence:guid}/file")]
    public async Task<IActionResult> Metadata(Guid organization,Guid incident,Guid evidence,CancellationToken ct)=>
        Ok(await service.FileMetadataAsync(Actor,organization,incident,evidence,ct));
    [HttpGet("{evidence:guid}/file/events")]
    public async Task<IActionResult> Events(Guid organization,Guid incident,Guid evidence,long afterRevision=0,int limit=50,CancellationToken ct=default)=>
        Ok(await service.FileEventsAsync(Actor,organization,incident,evidence,afterRevision,limit,ct));
    [HttpPost("{evidence:guid}/file/recovery")]
    public async Task<IActionResult> Recover(Guid organization,Guid incident,Guid evidence,RecoverEvidenceFile request,CancellationToken ct)
    {
        var result=await service.RecoverFileAsync(Actor,organization,incident,evidence,request,ct);
        return result.State=="AVAILABLE" ? Ok(result):StatusCode(202,result);
    }
    [HttpPost("{evidence:guid}/file/verification")]
    public async Task<ActionResult<EvidenceFileVerification>> Verify(Guid organization,Guid incident,Guid evidence,VerifyEvidenceFile request,CancellationToken ct) =>
        Ok(await service.VerifyFileAsync(Actor,organization,incident,evidence,request,ct));
    [HttpGet("{evidence:guid}/file/download")]
    public async Task<IActionResult> Download(Guid organization,Guid incident,Guid evidence,CancellationToken ct)
    {
        var result=await service.DownloadAsync(Actor,organization,incident,evidence,ct);
        Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers["Content-Security-Policy"]="sandbox";
        return File(result.Content,result.ContentType,result.OriginalName,false);
    }
}
