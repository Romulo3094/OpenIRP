using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record RegisterFileEvidence([property: JsonRequired] EvidenceClassification Classification,
    string Title, Guid? ExecutionId, Guid? TaskId, [property: JsonRequired] long ExpectedIncidentRevision, string Reason);
public sealed record RecoverEvidenceFile([property: JsonRequired] long ExpectedFileRevision, string Reason);
public sealed record EvidenceFileItem(Guid EvidenceId, string State, long Revision, string? OriginalName,
    string? ContentType, long Length, string? Sha256, string? FailureCode, DateTimeOffset ChangedAt);
public sealed record FileEvidenceResult(EvidenceResult Registration, EvidenceFileItem File);
public sealed record EvidenceDownload(Stream Content, string ContentType, string OriginalName, long Length);
public sealed record StagedEvidenceFile(Guid StorageId, string OriginalName, string ContentType, long Length, string Sha256);
public sealed class EvidenceStorageException(string code) : Exception(code) { public string Code { get; } = code; }

public interface IPrivateEvidenceStore
{
    Task<StagedEvidenceFile> StageAsync(Stream source, string name, string contentType, CancellationToken ct);
    Task PublishAsync(StagedEvidenceFile file, CancellationToken ct);
    Task<Stream> OpenVerifiedAsync(StagedEvidenceFile file, CancellationToken ct);
    Task DiscardUnregisteredAsync(Guid storageId);
}
public sealed record VerifyEvidenceFile([property: JsonRequired] long ExpectedFileRevision, string Reason);
public sealed record EvidenceFileVerification(Guid EvidenceId, long Revision, string State, bool PhysicalIntegrityVerified,
    bool DownloadAllowed, DateTimeOffset CheckedAt, string? FailureCode)
{
    public bool MalwareScanned => false;
    public bool TechnicalSufficiencyVerified => false;
    public string RetentionPolicy => "RETAIN_NO_AUTOMATIC_PURGE";
}
public interface IFileEvidenceService
{
    Task<EvidenceFileVerification> VerifyFileAsync(Guid actor, Guid organization, Guid incident, Guid evidence, VerifyEvidenceFile request, CancellationToken ct = default);
    Task<FileEvidenceResult> UploadAsync(Guid actor, Guid organization, Guid incident, RegisterFileEvidence request,
        Stream source, string name, string contentType, CancellationToken ct = default);
    Task<EvidenceFileItem> FileMetadataAsync(Guid actor, Guid organization, Guid incident, Guid evidence, CancellationToken ct = default);
    Task<EvidenceFileItem> RecoverFileAsync(Guid actor, Guid organization, Guid incident, Guid evidence, RecoverEvidenceFile request, CancellationToken ct = default);
    Task<EvidenceDownload> DownloadAsync(Guid actor, Guid organization, Guid incident, Guid evidence, CancellationToken ct = default);
    Task<IReadOnlyList<EvidenceFileEventItem>> FileEventsAsync(Guid actor, Guid organization, Guid incident, Guid evidence,
        long afterRevision = 0, int limit = 50, CancellationToken ct = default);
}
public sealed record EvidenceFileEventItem(long Revision, string State, string? FailureCode, Guid ActorUserId,
    Guid CorrelationId, string Reason, DateTimeOffset OccurredAt);
