using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class DependencyExceptionMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<DependencyException>(b =>
        {
            b.ToTable("dependency_exceptions", "app", t =>
            {
                t.HasCheckConstraint("ck_dependency_exception_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                t.HasCheckConstraint("ck_dependency_exception_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>'' AND btrim(impact)<>'' AND btrim(residual_risk)<>'' AND jsonb_typeof(dependency_context_json)='object' AND octet_length(dependency_context_json::text)<=262144");
                t.HasCheckConstraint("ck_dependency_exception_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
            });
            b.Property(e => e.Impact).HasMaxLength(2000);
            b.Property(e => e.ResidualRisk).HasMaxLength(2000);
            b.Property(e => e.DependencyContextJson).HasColumnType("jsonb");
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.DependencyId, e.PredecessorTaskId, e.SkipAuthorizationId, e.AssignedMembershipId, e.Id }).HasName("ak_dependency_exception_scope");
            b.Property(e => e.Action).HasMaxLength(20); b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.SelfApprovalJustification).HasMaxLength(2000);
            b.HasIndex(e => new { e.TaskId, e.PredecessorTaskId, e.ExecutionRevision }).IsUnique();
            b.HasIndex(e => e.RequestId).IsUnique().HasFilter("request_id IS NOT NULL");
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DependencyException>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.DependencyId, e.PredecessorTaskId, e.SkipAuthorizationId, e.AssignedMembershipId, e.RequestId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.DependencyId, e.PredecessorTaskId, e.SkipAuthorizationId, e.AssignedMembershipId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.PredecessorTaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookStepDependency>().WithMany().HasForeignKey(e => e.DependencyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SkipAuthorization>().WithMany().HasForeignKey(e => e.SkipAuthorizationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
