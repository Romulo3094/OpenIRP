using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;
namespace OpenIRP.Infrastructure.Persistence.Configurations;
internal static class AssistanceMapping
{
    internal static void Apply(ModelBuilder model)=>model.Entity<AssistanceAssessment>(b=>{
        b.ToTable("assistance_assessments","app",t=>{
            t.HasCheckConstraint("ck_assistance_values","kind='EXPLAIN_STEP' AND schema_version='openirp-assistance-v1' AND prompt_version='openirp-explain-step-v1' AND incident_revision>0 AND execution_revision>0 AND duration_ms BETWEEN 0 AND 600000 AND context_token ~ '^risk-v1:[0-9a-f]{64}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (model_digest IS NULL OR model_digest ~ '^[0-9a-f]{64}$')");
            t.HasCheckConstraint("ck_assistance_context","jsonb_typeof(context_json::jsonb)='object' AND octet_length(context_json)<=32768 AND input_hash=encode(sha256(convert_to(context_json,'UTF8')),'hex')");
            t.HasCheckConstraint("ck_assistance_outcome","(outcome='VALID' AND error_code IS NULL AND recommendation_json IS NOT NULL AND jsonb_typeof(recommendation_json::jsonb)='object' AND octet_length(recommendation_json)<=16384) OR (outcome IN ('INVALID','UNAVAILABLE','STALE') AND error_code IS NOT NULL AND recommendation_json IS NULL)");
        });
        b.Property(x=>x.ParametersJson).HasColumnType("text");b.Property(x=>x.Reason).HasMaxLength(2000);
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
        b.Property(x=>x.ContextToken).HasMaxLength(72);b.Property(x=>x.InputHash).HasMaxLength(64);
        b.Property(x=>x.ContextJson).HasColumnType("text");b.Property(x=>x.RecommendationJson).HasColumnType("text");
        b.Property(x=>x.Model).HasMaxLength(100);b.Property(x=>x.ModelDigest).HasMaxLength(64);
        b.Property(x=>x.SchemaVersion).HasMaxLength(50);b.Property(x=>x.PromptVersion).HasMaxLength(50);
        b.Property(x=>x.Kind).HasMaxLength(30);b.Property(x=>x.Outcome).HasMaxLength(20);b.Property(x=>x.ErrorCode).HasMaxLength(60);
        b.HasIndex(x=>new{x.OrganizationId,x.IncidentId,x.ExecutionId,x.TaskId,x.OccurredAt,x.Id});
        b.HasOne<ExecutionTask>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.IncidentId,x.VersionId,x.ExecutionId,x.TaskId})
            .HasPrincipalKey(x=>new{x.OrganizationId,x.IncidentId,x.VersionId,x.ExecutionId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    });
}
