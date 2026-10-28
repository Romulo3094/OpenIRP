using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class SkipAuthorizationMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<SkipAuthorization>(b =>
        {
            b.ToTable("skip_authorizations", "app", t =>
            {
                t.HasCheckConstraint("ck_skip_authorization_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                t.HasCheckConstraint("ck_skip_authorization_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>'' AND btrim(impact)<>'' AND btrim(residual_risk)<>'' AND jsonb_typeof(dependency_context_json)='object' AND octet_length(dependency_context_json::text)<=65536");
                t.HasCheckConstraint("ck_skip_authorization_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
            });
            b.Property(e => e.Impact).HasMaxLength(2000);
            b.Property(e => e.ResidualRisk).HasMaxLength(2000);
            b.Property(e => e.DependencyContextJson).HasColumnType("jsonb");
            b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.Id }).HasName("ak_skip_authorization_scope");
            b.Property(e => e.Action).HasMaxLength(20); b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.SelfApprovalJustification).HasMaxLength(2000);
            b.HasIndex(e => new { e.TaskId, e.ExecutionRevision }).IsUnique();
            b.HasIndex(e => e.RequestId).IsUnique().HasFilter("request_id IS NOT NULL");
            b.HasOne<ExecutionTask>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SkipAuthorization>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.RequestId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.IncidentId, e.VersionId, e.ExecutionId, e.TaskId, e.AssignedMembershipId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
