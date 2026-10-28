using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class EvidenceFileMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<EvidenceFile>(b=>
        {
            b.ToTable("evidence_files","app",t=>
            {
                t.HasCheckConstraint("ck_file_metadata","length>0 AND length<=10485760 AND sha256 ~ '^[0-9a-f]{64}$' AND btrim(original_name)<>'' AND content_type IN ('application/pdf','image/png','image/jpeg','text/plain')");
                t.HasCheckConstraint("ck_file_state","revision>0 AND changed_at>=created_at AND ((state IN ('PENDING','AVAILABLE') AND failure_code IS NULL) OR (state IN ('FAILED','QUARANTINED') AND failure_code IN ('missing_file','storage_commit_failed','integrity_check_failed','unsafe_storage_path','withdrawn')))");
            });
            b.HasKey(f=>f.EvidenceId);b.Property(f=>f.EvidenceId).ValueGeneratedNever();
            b.HasAlternateKey(f=>new{f.OrganizationId,f.IncidentId,f.EvidenceId});
            b.HasIndex(f=>f.StorageId).IsUnique();
            b.Property(f=>f.OriginalName).HasMaxLength(200);b.Property(f=>f.ContentType).HasMaxLength(100);
            b.Property(f=>f.Sha256).HasMaxLength(64);b.Property(f=>f.State).HasMaxLength(20);b.Property(f=>f.FailureCode).HasMaxLength(40);
            b.Property(f=>f.Revision).IsConcurrencyToken();
            b.HasOne<IncidentEvidence>().WithMany().HasForeignKey(f=>new{f.OrganizationId,f.IncidentId,f.EvidenceId})
                .HasPrincipalKey(e=>new{e.OrganizationId,e.IncidentId,e.Id}).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EvidenceFileEvent>(b=>
        {
            b.ToTable("evidence_file_events","app",t=>t.HasCheckConstraint("ck_file_event","revision>0 AND btrim(reason)<>'' AND state IN ('PENDING','AVAILABLE','FAILED','QUARANTINED')"));
            b.HasKey(f=>f.Id);b.Property(f=>f.Id).ValueGeneratedNever();
            b.Property(f=>f.State).HasMaxLength(20);b.Property(f=>f.FailureCode).HasMaxLength(40);b.Property(f=>f.Reason).HasMaxLength(2000);
            b.HasIndex(f=>new{f.EvidenceId,f.Revision}).IsUnique();
            b.HasOne<EvidenceFile>().WithMany().HasForeignKey(f=>new{f.OrganizationId,f.IncidentId,f.EvidenceId})
                .HasPrincipalKey(f=>new{f.OrganizationId,f.IncidentId,f.EvidenceId}).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(f=>f.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
