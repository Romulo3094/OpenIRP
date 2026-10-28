using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class ExecutionMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<PlaybookExecution>(b =>
        {
            b.ToTable("playbook_executions", "app", t =>
            {
                t.HasCheckConstraint("ck_execution_status", "status IN ('IN_PROGRESS','COMPLETED','CANCELLED')");
                t.HasCheckConstraint("ck_execution_revision", "revision > 0");
                t.HasCheckConstraint("ck_execution_finish", "(status='IN_PROGRESS' AND finished_at IS NULL AND finish_reason IS NULL) OR (status<>'IN_PROGRESS' AND finished_at IS NOT NULL AND finished_at >= created_at AND finish_reason IS NOT NULL AND btrim(finish_reason)<>'')");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.Id });
            b.Property(e => e.Status).HasMaxLength(20); b.Property(e => e.FinishReason).HasMaxLength(2000);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId }).IsUnique().HasFilter("status='IN_PROGRESS'").HasDatabaseName("ux_incident_active_execution");
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e => new { e.PlaybookId, e.VersionId })
                .HasPrincipalKey(e => new { e.PlaybookId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExecutionTask>(b =>
        {
            b.ToTable("execution_tasks", "app", t =>
            {
                t.HasCheckConstraint("ck_execution_task_status", "status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED')");
                t.HasCheckConstraint("ck_execution_task_revision", "changed_at_revision > 0");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id });
            b.Property(e => e.Status).HasMaxLength(20);
            b.HasIndex(e => new { e.ExecutionId, e.StepId }).IsUnique();
            b.HasOne<PlaybookExecution>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookStep>().WithMany().HasForeignKey(e => new { e.VersionId, e.StepId })
                .HasPrincipalKey(e => new { e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExecutionTaskEvent>(b =>
        {
            b.ToTable("execution_task_events", "app", t =>
            {
                t.HasCheckConstraint("ck_execution_event_action", "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED')");
                t.HasCheckConstraint("ck_execution_event_values", "revision > 0 AND incident_revision > 0 AND btrim(reason)<>'' AND execution_status IN ('IN_PROGRESS','COMPLETED','CANCELLED')");
                t.HasCheckConstraint("ck_execution_event_task", "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED'))");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Action).HasMaxLength(40); b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.ExecutionStatus).HasMaxLength(20); b.Property(e => e.TaskStatus).HasMaxLength(20);
            b.HasIndex(e => new { e.ExecutionId, e.Revision }).IsUnique();
            b.HasOne<PlaybookExecution>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
