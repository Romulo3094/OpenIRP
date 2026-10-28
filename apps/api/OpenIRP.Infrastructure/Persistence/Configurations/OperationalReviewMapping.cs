using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;
namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class OperationalReviewMapping
{
    internal static void Apply(ModelBuilder model) => model.Entity<OperationalReview>(b =>
    {
        b.ToTable("operational_reviews", "app", t =>
        {
            t.HasCheckConstraint("ck_operational_review_values", "kind IN ('EVIDENCE','ALTERNATIVE') AND btrim(method)<>'' AND btrim(assessment)<>'' AND btrim(reason)<>'' AND incident_revision>1 AND context_token ~ '^ops-v1:[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_operational_review_alternative", "(kind='EVIDENCE' AND alternative_procedure IS NULL) OR (kind='ALTERNATIVE' AND alternative_procedure IS NOT NULL AND btrim(alternative_procedure)<>'')");
            t.HasCheckConstraint("ck_operational_review_self", "(self_review AND self_review_justification IS NOT NULL AND btrim(self_review_justification)<>'') OR (NOT self_review AND self_review_justification IS NULL)");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Kind).HasMaxLength(20); b.Property(r => r.Method).HasMaxLength(2000);
        b.Property(r => r.Assessment).HasMaxLength(4000); b.Property(r => r.AlternativeProcedure).HasMaxLength(4000);
        b.Property(r => r.ContextToken).HasMaxLength(72); b.Property(r => r.Reason).HasMaxLength(2000);
        b.Property(r => r.SelfReviewJustification).HasMaxLength(2000);
        b.HasIndex(r => new { r.OrganizationId,r.IncidentId,r.IncidentRevision }).IsUnique();
        b.HasIndex(r => new { r.ExecutionId,r.TaskId,r.Kind,r.IncidentRevision });
        b.HasOne<ExecutionTask>().WithMany().HasForeignKey(r => new {r.OrganizationId,r.IncidentId,r.TaskId})
            .HasPrincipalKey(t => new {t.OrganizationId,t.IncidentId,t.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<IncidentEvidence>().WithMany().HasForeignKey(r => new {r.OrganizationId,r.IncidentId,r.EvidenceId})
            .HasPrincipalKey(e => new {e.OrganizationId,e.IncidentId,e.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    });
}
