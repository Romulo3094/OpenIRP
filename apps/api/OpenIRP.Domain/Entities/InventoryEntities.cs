namespace OpenIRP.Domain.Entities;

public interface ICatalogRecord
{
    Guid Id { get; set; }
    string Code { get; set; }
    string Name { get; set; }
    string Category { get; set; }
    string? Description { get; set; }
    bool IsActive { get; set; }
    long Revision { get; set; }
}

public sealed class CapabilityCatalogEntry : ICatalogRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public long Revision { get; set; } = 1;
}

public sealed class IncidentCategory : ICatalogRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public long Revision { get; set; } = 1;
}

public enum CapabilityAvailability { Unknown, Available, Unavailable }
public enum AssetKind { Server, Workstation, CloudService, Account, Application, NetworkDevice, CriticalService, Other }
public enum AssetCriticality { Low, Medium, High }
public enum AssetStatus { Active, Retired }

public sealed class OrganizationCapability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CapabilityId { get; set; }
    public CapabilityAvailability Availability { get; set; } = CapabilityAvailability.Unknown;
    public string? Notes { get; set; }
    public long Revision { get; set; } = 1;
}

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public AssetKind Kind { get; set; }
    public AssetCriticality Criticality { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public string? Description { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
