using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class InventoryMapping
{
    internal static void Apply(ModelBuilder model)
    {
        Catalog<CapabilityCatalogEntry>(model, "capability_catalog");
        Catalog<IncidentCategory>(model, "incident_categories");
        model.Entity<OrganizationCapability>(b =>
        {
            b.ToTable("organization_capabilities", "app", t =>
            {
                t.HasCheckConstraint("ck_org_capability_availability", "availability IN ('UNKNOWN','AVAILABLE','UNAVAILABLE')");
                t.HasCheckConstraint("ck_org_capability_revision", "revision > 0");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Availability).HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<CapabilityAvailability>(v, true)).HasMaxLength(20);
            b.Property(e => e.Notes).HasMaxLength(2000);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasIndex(e => new { e.OrganizationId, e.CapabilityId }).IsUnique();
            b.HasAlternateKey(e => new { e.OrganizationId, e.Id });
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CapabilityCatalogEntry>().WithMany().HasForeignKey(e => e.CapabilityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Asset>(b =>
        {
            b.ToTable("assets", "app", t =>
            {
                t.HasCheckConstraint("ck_asset_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_asset_kind", "kind IN ('SERVER','WORKSTATION','CLOUDSERVICE','ACCOUNT','APPLICATION','NETWORKDEVICE','CRITICALSERVICE','OTHER')");
                t.HasCheckConstraint("ck_asset_criticality", "criticality IN ('LOW','MEDIUM','HIGH')");
                t.HasCheckConstraint("ck_asset_status", "status IN ('ACTIVE','RETIRED')");
                t.HasCheckConstraint("ck_asset_revision", "revision > 0");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
            b.Property(e => e.Description).HasMaxLength(2000);
            b.Property(e => e.Kind).HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<AssetKind>(v, true)).HasMaxLength(20);
            b.Property(e => e.Criticality).HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<AssetCriticality>(v, true)).HasMaxLength(20);
            b.Property(e => e.Status).HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<AssetStatus>(v, true)).HasMaxLength(20);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasAlternateKey(e => new { e.OrganizationId, e.Id });
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        var codes = new[] { "IT_STAFF", "SECURITY_SPECIALIST", "EXTERNAL_SUPPORT", "ENDPOINT_PROTECTION", "SECURITY_LOGS", "MFA", "BACKUPS", "DEVICE_ISOLATION" };
        var names = new[] { "Personal de TI", "Especialista de seguridad", "Soporte externo", "Protección de endpoints", "Registros de seguridad", "Autenticación multifactor", "Respaldos", "Aislamiento de dispositivos" };
        var categories = new[] { "PERSONNEL", "PERSONNEL", "PERSONNEL", "PROTECTION", "VISIBILITY", "ACCESS", "RECOVERY", "RESPONSE" };
        model.Entity<CapabilityCatalogEntry>().HasData(codes.Select((code, i) => new CapabilityCatalogEntry
        { Id = Guid.Parse($"26000000-0000-4000-8000-{i + 1:000000000000}"), Code = code, Name = names[i], Category = categories[i], Revision = 1, IsActive = true }));
        model.Entity<IncidentCategory>().HasData(
            new IncidentCategory { Id = Guid.Parse("26000000-0000-4000-8100-000000000001"), Code = "PHISHING", Name = "Phishing", Category = "SOCIAL_ENGINEERING" },
            new IncidentCategory { Id = Guid.Parse("26000000-0000-4000-8100-000000000002"), Code = "ACCOUNT_COMPROMISE", Name = "Compromiso de cuenta", Category = "IDENTITY" },
            new IncidentCategory { Id = Guid.Parse("26000000-0000-4000-8100-000000000003"), Code = "RANSOMWARE", Name = "Ransomware", Category = "MALWARE" });
    }

    private static void Catalog<T>(ModelBuilder model, string table) where T : class, ICatalogRecord
    {
        model.Entity<T>(b =>
        {
            b.ToTable(table, "app", t =>
            {
                t.HasCheckConstraint($"ck_{table}_code", "code ~ '^[A-Z][A-Z0-9_]{1,59}$'");
                t.HasCheckConstraint($"ck_{table}_name", "btrim(name) <> '' AND btrim(category) <> ''");
                t.HasCheckConstraint($"ck_{table}_revision", "revision > 0");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Code).HasMaxLength(60).IsRequired();
            b.Property(e => e.Name).HasMaxLength(200).IsRequired();
            b.Property(e => e.Category).HasMaxLength(60).IsRequired();
            b.Property(e => e.Description).HasMaxLength(2000);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasIndex(e => e.Code).IsUnique();
        });
    }
}
