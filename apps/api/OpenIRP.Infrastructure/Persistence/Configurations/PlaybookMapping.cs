using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class PlaybookMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<Playbook>(b =>
        {
            b.ToTable("playbooks", "app", t =>
            {
                t.HasCheckConstraint("ck_playbook_revision", "revision > 0");
                t.HasCheckConstraint("ck_playbook_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_playbook_clone_origin", "(source_version_id IS NULL AND source_revision IS NULL AND clone_reason IS NULL) OR (source_version_id IS NOT NULL AND source_revision IS NOT NULL AND source_revision > 0 AND clone_reason IS NOT NULL AND btrim(clone_reason) <> '')");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.Property(e => e.CloneReason).HasMaxLength(2000);
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => e.SourceVersionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_playbook_clone_source_version");
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => new { e.Id, e.CurrentPublishedVersionId })
                .HasPrincipalKey(e => new { e.PlaybookId, e.Id }).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_playbook_current_version");
            b.HasIndex(e => new { e.OrganizationId, e.Id });
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlaybookVersion>(b =>
        {
            b.ToTable("playbook_versions", "app", t =>
            {

                t.HasCheckConstraint("ck_playbook_version_state", "version_number > 0 AND status IN ('DRAFT','IN_REVIEW','PUBLISHED','ARCHIVED')");
                t.HasCheckConstraint("ck_playbook_version_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_playbook_version_metadata", "(status = 'DRAFT' AND review_request_id IS NULL AND submitted_by_user_id IS NULL AND published_at IS NULL) OR (status = 'IN_REVIEW' AND review_request_id IS NOT NULL AND submitted_by_user_id IS NOT NULL AND published_at IS NULL) OR (status IN ('PUBLISHED','ARCHIVED') AND review_request_id IS NOT NULL AND submitted_by_user_id IS NOT NULL AND published_at IS NOT NULL)");
                t.HasCheckConstraint("ck_playbook_version_source", "source_version_id IS NULL OR source_version_id <> id");
                t.HasCheckConstraint("ck_playbook_version_content", "btrim(objective) <> '' AND btrim(instructions) <> ''");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Status).HasMaxLength(20).IsRequired();
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.HasAlternateKey(e => new { e.PlaybookId, e.Id });
            b.HasIndex(e => e.PlaybookId).IsUnique().HasFilter("status IN ('DRAFT','IN_REVIEW')")
                .HasDatabaseName("ux_playbook_active_draft");
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => new { e.PlaybookId, e.SourceVersionId })
                .HasPrincipalKey(e => new { e.PlaybookId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.Objective).HasMaxLength(4000).IsRequired();
            b.Property(e => e.Instructions).HasMaxLength(12000).IsRequired();
            b.HasIndex(e => new { e.PlaybookId, e.VersionNumber }).IsUnique();
            b.HasOne<Playbook>().WithMany().HasForeignKey(e => e.PlaybookId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlaybookReview>(b =>
        {
            b.ToTable("playbook_reviews", "app", t =>
            {
                t.HasCheckConstraint("ck_playbook_review_action", "action IN ('SUBMITTED','PUBLISHED','REJECTED','WITHDRAWN')");
                t.HasCheckConstraint("ck_playbook_review_fields", "revision > 0 AND btrim(reason) <> '' AND content_hash ~ '^[A-F0-9]{64}$' AND ((action = 'WITHDRAWN' AND request_id IS NULL) OR (action <> 'WITHDRAWN' AND request_id IS NOT NULL))");
                t.HasCheckConstraint("ck_playbook_review_self", "(self_approval AND action = 'PUBLISHED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification) <> '') OR (NOT self_approval AND self_approval_justification IS NULL)");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Action).HasMaxLength(20).IsRequired();
            b.Property(e => e.Reason).HasMaxLength(2000).IsRequired();
            b.Property(e => e.ContentHash).HasMaxLength(64).IsRequired();
            b.Property(e => e.SelfApprovalJustification).HasMaxLength(2000);
            b.HasIndex(e => new { e.PlaybookId, e.Revision }).IsUnique();
            b.HasIndex(e => new { e.RequestId, e.Action }).IsUnique().HasFilter("request_id IS NOT NULL");
            b.HasIndex(e => e.RequestId).IsUnique().HasFilter("action IN ('PUBLISHED','REJECTED')")
                .HasDatabaseName("ux_playbook_review_decision");
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => new { e.PlaybookId, e.VersionId })
                .HasPrincipalKey(e => new { e.PlaybookId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlaybookStep>(b =>
        {
            b.ToTable("playbook_steps", "app", t =>
            {
                t.HasCheckConstraint("ck_playbook_step_code", "code ~ '^[A-Z][A-Z0-9_]{0,59}$'");
                t.HasCheckConstraint("ck_playbook_step_content", "position BETWEEN 1 AND 10000 AND btrim(phase) <> '' AND btrim(instructions) <> ''");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.VersionId, e.Id });
            b.Property(e => e.Code).HasMaxLength(60).IsRequired();
            b.Property(e => e.Phase).HasMaxLength(60).IsRequired();
            b.Property(e => e.Instructions).HasMaxLength(8000).IsRequired();
            b.HasIndex(e => new { e.VersionId, e.Code }).IsUnique();
            b.HasIndex(e => new { e.VersionId, e.Position }).IsUnique();
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => e.VersionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlaybookStepDependency>(b =>
        {
            b.ToTable("playbook_step_dependencies", "app", t => t.HasCheckConstraint("ck_playbook_dependency_self", "predecessor_id <> successor_id"));
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasIndex(e => new { e.VersionId, e.PredecessorId, e.SuccessorId }).IsUnique();
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => e.VersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookStep>().WithMany().HasForeignKey(e => new { e.VersionId, e.PredecessorId })
                .HasPrincipalKey(e => new { e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookStep>().WithMany().HasForeignKey(e => new { e.VersionId, e.SuccessorId })
                .HasPrincipalKey(e => new { e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlaybookCapabilityRequirement>(b =>
        {
            b.ToTable("playbook_capability_requirements", "app", t => t.HasCheckConstraint("ck_playbook_requirement_level", "level IN ('REQUIRED','RECOMMENDED')"));
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Level).HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<PlaybookRequirementLevel>(v, true)).HasMaxLength(20);
            b.HasIndex(e => new { e.VersionId, e.CapabilityId }).IsUnique().HasFilter("step_id IS NULL")
                .HasDatabaseName("ux_playbook_requirement_version");
            b.HasIndex(e => new { e.VersionId, e.StepId, e.CapabilityId }).IsUnique().HasFilter("step_id IS NOT NULL")
                .HasDatabaseName("ux_playbook_requirement_step");
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => e.VersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookStep>().WithMany().HasForeignKey(e => new { e.VersionId, e.StepId })
                .HasPrincipalKey(e => new { e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CapabilityCatalogEntry>().WithMany().HasForeignKey(e => e.CapabilityId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
