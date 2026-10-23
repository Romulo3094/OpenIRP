namespace OpenIRP.Domain.Entities;

// Reported fields describe the original observation, never the current inventory record.
public sealed class IncidentAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? AssetId { get; set; }
    public string ReportedName { get; set; } = "";
    public AssetKind ReportedType { get; set; }
    public string? ReportedContext { get; set; }
    public AssetCriticality? ReportedCriticality { get; set; }
    public long ReportedAtRevision { get; set; }
    public long? LinkedAtRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
