using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class IncidentCapabilityMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<IncidentCapabilityAssessment>(b =>
        {
            b.ToTable("incident_capability_assessments", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_capability_availability", "availability IN ('UNKNOWN','AVAILABLE','UNAVAILABLE')");
                t.HasCheckConstraint("ck_incident_capability_revision", "assessed_at_revision > 1");
                t.HasCheckConstraint("ck_incident_capability_reason", "btrim(reason) <> ''");
                t.HasCheckConstraint("ck_incident_capability_limitations", "limitations IS NULL OR btrim(limitations) <> ''");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Availability).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<CapabilityAvailability>(v, true)).HasMaxLength(20);
            b.Property(e => e.Limitations).HasMaxLength(2000);
            b.Property(e => e.Reason).HasMaxLength(2000).IsRequired();
            b.Property(e => e.AssessedAtRevision).IsConcurrencyToken();
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.CapabilityId }).IsUnique();
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CapabilityCatalogEntry>().WithMany().HasForeignKey(e => e.CapabilityId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
