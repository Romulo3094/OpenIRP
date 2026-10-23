using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class IncidentAssetMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<IncidentAsset>(b =>
        {
            b.ToTable("incident_assets", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_asset_name", "btrim(reported_name) <> ''");
                t.HasCheckConstraint("ck_incident_asset_type", "reported_type IN ('SERVER','WORKSTATION','CLOUDSERVICE','ACCOUNT','APPLICATION','NETWORKDEVICE','CRITICALSERVICE','OTHER')");
                t.HasCheckConstraint("ck_incident_asset_criticality", "reported_criticality IN ('LOW','MEDIUM','HIGH')");
                t.HasCheckConstraint("ck_incident_asset_revisions", "reported_at_revision > 1 AND (linked_at_revision IS NULL OR linked_at_revision >= reported_at_revision)");
                t.HasCheckConstraint("ck_incident_asset_link", "(asset_id IS NULL AND linked_at_revision IS NULL) OR (asset_id IS NOT NULL AND linked_at_revision IS NOT NULL)");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.ReportedName).HasMaxLength(200).IsRequired();
            b.Property(e => e.ReportedContext).HasMaxLength(2000);
            b.Property(e => e.ReportedType).HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<AssetKind>(v, true)).HasMaxLength(20);
            b.Property(e => e.ReportedCriticality).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<AssetCriticality>(v, true)).HasMaxLength(20);
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.Id });
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.AssetId }).IsUnique().HasFilter("asset_id IS NOT NULL");
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Asset>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssetId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
