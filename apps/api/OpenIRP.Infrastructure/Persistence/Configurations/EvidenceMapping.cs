using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class EvidenceMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<IncidentEvidence>(b =>
        {
            b.ToTable("incident_evidence", "app", t =>
            {
                t.HasCheckConstraint("ck_evidence_content", "kind IN ('TEXT','REFERENCE','FILE') AND classification IN ('INTERNAL','CONFIDENTIAL') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");
                t.HasCheckConstraint("ck_evidence_task_scope", "(version_id IS NULL AND execution_id IS NULL AND task_id IS NULL) OR (version_id IS NOT NULL AND execution_id IS NOT NULL AND task_id IS NOT NULL)");
                t.HasCheckConstraint("ck_evidence_lifecycle", "(status='ACTIVE' AND revision=1 AND withdrawn_by_user_id IS NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL) OR (status='WITHDRAWN' AND revision=2 AND withdrawn_by_user_id IS NOT NULL AND withdrawn_at IS NOT NULL AND withdrawn_at>=created_at AND withdrawal_reason IS NOT NULL AND btrim(withdrawal_reason)<>'')");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.Id });
            b.Property(e => e.Kind).HasMaxLength(20); b.Property(e => e.Classification).HasMaxLength(20);
            b.Property(e => e.Status).HasMaxLength(20); b.Property(e => e.Title).HasMaxLength(200);
            b.Property(e => e.Content).HasMaxLength(16000); b.Property(e => e.WithdrawalReason).HasMaxLength(2000);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.Id });
            b.HasIndex(e => new { e.TaskId, e.Status });
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.WithdrawnByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExecutionTask>(b =>
        {
            b.ToTable("execution_tasks", "app", t => t.HasCheckConstraint("ck_task_completion_evidence", "completion_evidence_id IS NULL OR status='COMPLETED'"));
            b.HasOne<IncidentEvidence>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.CompletionEvidenceId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EvidenceEvent>(b =>
        {
            b.ToTable("evidence_events", "app", t => t.HasCheckConstraint("ck_evidence_event", "(action='REGISTERED' AND revision=1 OR action='WITHDRAWN' AND revision=2) AND btrim(reason)<>''"));
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Action).HasMaxLength(20); b.Property(e => e.Reason).HasMaxLength(2000);
            b.HasIndex(e => new { e.EvidenceId, e.Revision }).IsUnique();
            b.HasOne<IncidentEvidence>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.EvidenceId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
