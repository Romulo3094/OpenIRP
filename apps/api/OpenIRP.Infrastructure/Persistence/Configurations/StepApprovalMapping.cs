using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class StepApprovalMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<StepApproval>(b =>
        {
            b.ToTable("step_approvals", "app", t =>
            {
                t.HasCheckConstraint("ck_step_approval_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                t.HasCheckConstraint("ck_step_approval_values", "execution_revision>1 AND btrim(reason)<>''");
                t.HasCheckConstraint("ck_step_approval_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
            });
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.Id }).HasName("ak_step_approval_scope");
            b.Property(e => e.Action).HasMaxLength(20); b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.SelfApprovalJustification).HasMaxLength(2000);
            b.HasIndex(e => new { e.TaskId, e.ExecutionRevision }).IsUnique();
            b.HasIndex(e => e.RequestId).IsUnique().HasFilter("request_id IS NOT NULL");
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StepApproval>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.RequestId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
