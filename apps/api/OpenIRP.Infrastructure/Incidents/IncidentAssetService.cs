using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Incidents;

public sealed partial class IncidentService
{
    private static readonly JsonSerializerOptions DetailsOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public async Task<IncidentAssetChange> ReportAssetAsync(Guid actor, Guid organization, Guid incident, ReportIncidentAsset request, CancellationToken ct = default)
    {
        IncidentAsset row = null!;
        var result = await WriteAsync(actor, organization, incident, "ASSET_REPORTED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if (string.IsNullOrWhiteSpace(request.ReportedName) || request.ReportedName.Length > 200
                || !Enum.IsDefined(request.ReportedType) || request.ReportedContext?.Length > 2000
                || (request.ReportedCriticality is { } criticality && !Enum.IsDefined(criticality)))
                throw Error(400, "invalid_reported_asset");
            row = new() { OrganizationId = organization, IncidentId = incident, ReportedName = request.ReportedName.Trim(),
                ReportedType = request.ReportedType, ReportedContext = request.ReportedContext,
                ReportedCriticality = request.ReportedCriticality, ReportedAtRevision = entity.Revision };
            db.IncidentAssets.Add(row);
            return (entity, request.Reason.Trim());
        }, ct, () => AssetDetails(row));
        return new(AssetView(row), result.Revision);
    }

    public async Task<IncidentAssetChange> AttachAssetAsync(Guid actor, Guid organization, Guid incident, AttachIncidentAsset request, CancellationToken ct = default)
    {
        IncidentAsset row = null!;
        var result = await WriteAsync(actor, organization, incident, "ASSET_ATTACHED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            if (request.ReportedContext?.Length > 2000) throw Error(400, "invalid_reported_asset");
            var asset = await AvailableAssetAsync(organization, request.AssetId, ct);
            await UniqueAssetAsync(organization, incident, asset.Id, ct);
            row = new() { OrganizationId = organization, IncidentId = incident, AssetId = asset.Id,
                ReportedName = asset.Name, ReportedType = asset.Kind, ReportedCriticality = asset.Criticality,
                ReportedContext = request.ReportedContext, ReportedAtRevision = entity.Revision, LinkedAtRevision = entity.Revision };
            db.IncidentAssets.Add(row);
            return (entity, request.Reason.Trim());
        }, ct, () => AssetDetails(row));
        return new(AssetView(row), result.Revision);
    }

    public async Task<IncidentAssetChange> LinkAssetAsync(Guid actor, Guid organization, Guid incident, Guid association, LinkIncidentAsset request, CancellationToken ct = default)
    {
        IncidentAsset row = null!;
        var result = await WriteAsync(actor, organization, incident, "ASSET_LINKED", async () =>
        {
            var entity = await FindAsync(organization, incident, ct);
            Reason(request.Reason); Next(entity, request.ExpectedRevision);
            row = await db.IncidentAssets.SingleOrDefaultAsync(a => a.OrganizationId == organization && a.IncidentId == incident && a.Id == association, ct)
                ?? throw Error(404, "incident_asset_not_found");
            if (row.AssetId is not null) throw Error(409, "incident_asset_already_linked");
            var asset = await AvailableAssetAsync(organization, request.AssetId, ct);
            await UniqueAssetAsync(organization, incident, asset.Id, ct);
            // Preserve the original observation even when the inventory currently disagrees.
            row.AssetId = asset.Id; row.LinkedAtRevision = entity.Revision;
            return (entity, request.Reason.Trim());
        }, ct, () => AssetDetails(row));
        return new(AssetView(row), result.Revision);
    }

    public Task<IncidentPage<IncidentAssetItem>> AssetsAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default) =>
        ReadAsync(actor, organization, incident, async () =>
        {
            if (skip is < 0 or > 1000000 || limit is < 1 or > 100) throw Error(400, "invalid_incident_pagination");
            if (!await db.Incidents.AnyAsync(e => e.OrganizationId == organization && e.Id == incident, ct)) throw Error(404, "incident_not_found");
            var rows = await db.IncidentAssets.AsNoTracking().Where(e => e.OrganizationId == organization && e.IncidentId == incident)
                .OrderBy(e => e.Id).Skip(skip).Take(limit + 1).ToListAsync(ct);
            return new IncidentPage<IncidentAssetItem>(rows.Take(limit).Select(AssetView).ToList(), rows.Count > limit ? skip + limit : null);
        }, ct);

    private async Task<Asset> AvailableAssetAsync(Guid organization, Guid asset, CancellationToken ct)
    {
        var entity = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.OrganizationId == organization && a.Id == asset, ct)
            ?? throw Error(404, "asset_not_found");
        if (entity.Status != AssetStatus.Active) throw Error(409, "inactive_asset");
        return entity;
    }
    private async Task UniqueAssetAsync(Guid organization, Guid incident, Guid asset, CancellationToken ct)
    {
        if (await db.IncidentAssets.AnyAsync(a => a.OrganizationId == organization && a.IncidentId == incident && a.AssetId == asset, ct))
            throw Error(409, "asset_already_in_incident");
    }
    private static IncidentAssetItem AssetView(IncidentAsset e) => new(e.Id, e.IncidentId, e.AssetId, e.ReportedName, e.ReportedType,
        e.ReportedContext, e.ReportedCriticality, e.ReportedAtRevision, e.LinkedAtRevision, e.CreatedAt);
    private static string AssetDetails(IncidentAsset e) => JsonSerializer.Serialize(new { asset = AssetView(e) }, DetailsOptions);
}
