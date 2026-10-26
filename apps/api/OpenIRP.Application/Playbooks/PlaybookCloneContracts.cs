using System.Text.Json.Serialization;

namespace OpenIRP.Application.Playbooks;

public sealed record ClonePlaybook([property: JsonRequired] Guid SourcePlaybookId,
    [property: JsonRequired] Guid SourceVersionId, [property: JsonRequired] long ExpectedSourceRevision, string Reason);
public sealed record PlaybookOriginItem(Guid PlaybookId, Guid? SourcePlaybookId, Guid? SourceVersionId,
    long? SourceRevision, string? Reason, Guid CreatedByUserId, DateTimeOffset CreatedAt);
