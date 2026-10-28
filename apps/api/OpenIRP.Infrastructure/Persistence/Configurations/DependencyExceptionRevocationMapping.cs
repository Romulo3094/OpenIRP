using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class DependencyExceptionRevocationMapping
{
    internal static void Apply(ModelBuilder model) => model.Entity<DependencyExceptionRevocation>(b =>
    {
        b.ToTable("dependency_exception_revocations", "app", t =>
            t.HasCheckConstraint("ck_dependency_revocation_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>''"));
        b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Reason).HasMaxLength(2000);
        b.HasIndex(e => e.ApprovalId).IsUnique();
        b.HasIndex(e => new { e.TaskId, e.ExecutionRevision }).IsUnique();
        b.HasOne<DependencyException>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId,e.IncidentId,e.VersionId,e.ExecutionId,e.TaskId,e.DependencyId,e.PredecessorTaskId,e.SkipAuthorizationId,e.AssignedMembershipId,e.ApprovalId })
            .HasPrincipalKey(e => new { e.OrganizationId,e.IncidentId,e.VersionId,e.ExecutionId,e.TaskId,e.DependencyId,e.PredecessorTaskId,e.SkipAuthorizationId,e.AssignedMembershipId,e.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    });
}
