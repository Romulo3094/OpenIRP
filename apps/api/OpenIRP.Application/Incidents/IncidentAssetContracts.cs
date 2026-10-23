using System.Text.Json.Serialization;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Application.Incidents;

public sealed record ReportIncidentAsset(string ReportedName, [property: JsonRequired] AssetKind ReportedType,
    string? ReportedContext, [property: JsonRequired] AssetCriticality? ReportedCriticality,
    string Reason, [property: JsonRequired] long ExpectedRevision);
public sealed record AttachIncidentAsset([property: JsonRequired] Guid AssetId, string? ReportedContext,
    string Reason, [property: JsonRequired] long ExpectedRevision);
public sealed record LinkIncidentAsset([property: JsonRequired] Guid AssetId, string Reason, [property: JsonRequired] long ExpectedRevision);
public sealed record IncidentAssetItem(Guid Id, Guid IncidentId, Guid? AssetId, string ReportedName,
    AssetKind ReportedType, string? ReportedContext, AssetCriticality? ReportedCriticality,
    long ReportedAtRevision, long? LinkedAtRevision, DateTimeOffset CreatedAt);
public sealed record IncidentAssetChange(IncidentAssetItem Asset, long IncidentRevision);
