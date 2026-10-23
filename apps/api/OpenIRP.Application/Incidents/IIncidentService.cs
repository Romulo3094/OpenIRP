using OpenIRP.Domain.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIRP.Application.Incidents;

public sealed record IncidentCreate([property: JsonRequired] Guid CategoryId, string Title, string? Description);
public sealed record IncidentUpdate([property: JsonRequired] Guid CategoryId, string Title, string? Description,
    string Reason, [property: JsonRequired] long ExpectedRevision);
public sealed record IncidentAssignment([property: JsonRequired] Guid? AssignedMembershipId, string Reason,
    [property: JsonRequired] long ExpectedRevision);
public sealed record IncidentItem(Guid Id, Guid OrganizationId, Guid CategoryId, string Title, string? Description,
    string Status, Guid? AssignedMembershipId, long Revision, DateTimeOffset CreatedAt,
    ImpactLevel? ConfidentialityImpact, ImpactLevel? IntegrityImpact, ImpactLevel? AvailabilityImpact)
{
    public bool IsImpactAssessmentComplete => ConfidentialityImpact is not null && IntegrityImpact is not null && AvailabilityImpact is not null;
}
public sealed record IncidentEventItem(Guid Id, Guid IncidentId, long Revision, Guid ActorUserId, Guid CorrelationId,
    string Action, string? Reason, Guid CategoryId, string Title, string? Description, string Status,
    Guid? AssignedMembershipId, DateTimeOffset OccurredAt, JsonElement Details,
    ImpactLevel? ConfidentialityImpact, ImpactLevel? IntegrityImpact, ImpactLevel? AvailabilityImpact);
public sealed record IncidentPage<T>(IReadOnlyList<T> Items, int? NextSkip);
public sealed record IncidentEventPage(IReadOnlyList<IncidentEventItem> Items, long? NextAfterRevision);

public interface IIncidentService
{
    Task<IncidentItem> AssessImpactAsync(Guid actor, Guid organization, Guid incident, AssessIncidentImpact request, CancellationToken ct = default);
    Task<IncidentCapabilityPage> CapabilitiesAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<IncidentCapabilityChange> AssessCapabilityAsync(Guid actor, Guid organization, Guid incident, Guid capability, AssessIncidentCapability request, CancellationToken ct = default);
    Task<IncidentAssetChange> ReportAssetAsync(Guid actor, Guid organization, Guid incident, ReportIncidentAsset request, CancellationToken ct = default);
    Task<IncidentAssetChange> AttachAssetAsync(Guid actor, Guid organization, Guid incident, AttachIncidentAsset request, CancellationToken ct = default);
    Task<IncidentAssetChange> LinkAssetAsync(Guid actor, Guid organization, Guid incident, Guid association, LinkIncidentAsset request, CancellationToken ct = default);
    Task<IncidentPage<IncidentAssetItem>> AssetsAsync(Guid actor, Guid organization, Guid incident, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<IncidentItem> CreateAsync(Guid actor, Guid organization, IncidentCreate request, CancellationToken ct = default);
    Task<IncidentItem> UpdateAsync(Guid actor, Guid organization, Guid incident, IncidentUpdate request, CancellationToken ct = default);
    Task<IncidentItem> AssignAsync(Guid actor, Guid organization, Guid incident, IncidentAssignment request, CancellationToken ct = default);
    Task<IncidentItem> GetAsync(Guid actor, Guid organization, Guid incident, CancellationToken ct = default);
    Task<IncidentPage<IncidentItem>> ListAsync(Guid actor, Guid organization, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<IncidentEventPage> EventsAsync(Guid actor, Guid organization, Guid incident, long afterRevision = 0, int limit = 50, CancellationToken ct = default);
}
