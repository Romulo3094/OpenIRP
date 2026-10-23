using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class IncidentMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<Incident>(b =>
        {
            b.ToTable("incidents", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_incident_revision", "revision > 0");
                t.HasCheckConstraint("ck_incident_cia", "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");
                t.HasCheckConstraint("ck_incident_status", "status = 'REGISTERED'");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.Id });
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.Property(e => e.Description).HasMaxLength(4000);
            b.Property(e => e.Status).HasMaxLength(30).IsRequired();
            b.Property(e => e.ConfidentialityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.IntegrityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AvailabilityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IncidentEvent>(b =>
        {
            b.ToTable("incident_events", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_event_revision", "revision > 0");
                t.HasCheckConstraint("ck_incident_event_action", "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
                t.HasCheckConstraint("ck_incident_event_details", "jsonb_typeof(details_json) = 'object' AND octet_length(details_json::text) <= 32768");
                t.HasCheckConstraint("ck_incident_event_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_incident_event_cia", "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");
                t.HasCheckConstraint("ck_incident_event_status", "status = 'REGISTERED'");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.Property(e => e.Description).HasMaxLength(4000);
            b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.Status).HasMaxLength(30).IsRequired();
            b.Property(e => e.ConfidentialityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.IntegrityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AvailabilityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.Action).HasMaxLength(30).IsRequired();
            b.Property(e => e.DetailsJson).HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.Revision }).IsUnique();
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
