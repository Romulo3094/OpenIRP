using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class ClosureRiskMapping
{
    internal static void Apply(ModelBuilder model) => model.Entity<ClosureRiskReview>(b =>
    {
        b.ToTable("closure_risk_reviews", "app", t =>
        {
            t.HasCheckConstraint("ck_closure_risk_values", "incident_revision>1 AND btrim(assessment)<>'' AND btrim(reason)<>'' AND context_token ~ '^risk-v1:[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_closure_risk_self", "(self_review AND self_review_justification IS NOT NULL AND btrim(self_review_justification)<>'') OR (NOT self_review AND self_review_justification IS NULL)");
        });
        b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.ContextToken).HasMaxLength(72); b.Property(e => e.Assessment).HasMaxLength(4000);
        b.Property(e => e.Reason).HasMaxLength(2000); b.Property(e => e.SelfReviewJustification).HasMaxLength(2000);
        b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.IncidentRevision }).IsUnique();
        b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
            .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    });
}
