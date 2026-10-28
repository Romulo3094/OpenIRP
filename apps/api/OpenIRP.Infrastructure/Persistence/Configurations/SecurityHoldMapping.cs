using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;
namespace OpenIRP.Infrastructure.Persistence.Configurations;
internal static class SecurityHoldMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<PlaybookSecurityHold>(b =>
        {
            b.ToTable("playbook_security_holds","app",t=>t.HasCheckConstraint("ck_security_hold_values","revision>1 AND btrim(reason)<>''"));
            b.HasKey(e=>e.Id);b.Property(e=>e.Id).ValueGeneratedNever();b.Property(e=>e.Reason).HasMaxLength(2000);
            b.HasAlternateKey(e=>new{e.VersionId,e.Id});b.HasIndex(e=>new{e.VersionId,e.Revision}).IsUnique();
            b.HasOne<PlaybookVersion>().WithMany().HasForeignKey(e=>e.VersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Playbook>().WithMany().HasForeignKey(e=>e.PlaybookId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e=>e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExecutionSecurityDecision>(b =>
        {
            b.ToTable("execution_security_decisions","app",t=>t.HasCheckConstraint("ck_security_decision_values","action IN ('CONTINUE','CANCEL') AND execution_revision>1 AND incident_revision>1 AND btrim(reason)<>''"));
            b.HasKey(e=>e.Id);b.Property(e=>e.Id).ValueGeneratedNever();b.Property(e=>e.Reason).HasMaxLength(2000);b.Property(e=>e.Action).HasMaxLength(20);
            b.HasIndex(e=>new{e.ExecutionId,e.SecurityHoldId,e.Action}).IsUnique();b.HasIndex(e=>new{e.ExecutionId,e.ExecutionRevision}).IsUnique();
            b.HasOne<PlaybookSecurityHold>().WithMany().HasForeignKey(e=>e.SecurityHoldId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PlaybookExecution>().WithMany().HasForeignKey(e=>new{e.OrganizationId,e.IncidentId,e.VersionId,e.ExecutionId}).HasPrincipalKey(e=>new{e.OrganizationId,e.IncidentId,e.VersionId,e.Id}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e=>e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
