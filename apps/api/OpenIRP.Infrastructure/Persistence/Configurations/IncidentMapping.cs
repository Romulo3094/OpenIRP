using Microsoft.EntityFrameworkCore;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Identity;

namespace OpenIRP.Infrastructure.Persistence.Configurations;

internal static class IncidentMapping
{
    internal static void Apply(ModelBuilder model)
    {
        model.Entity<Incident>(b =>
        {
            b.ToTable("incidents", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_incident_revision", "revision > 0");
                t.HasCheckConstraint("ck_incident_cia", "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");
                t.HasCheckConstraint("ck_incident_context", "(scope IS NULL OR scope IN ('SINGLE','MULTIPLE','ORGANIZATIONWIDE')) AND (operational_impact IS NULL OR operational_impact IN ('NONE','DEGRADED','MAJORINTERRUPTION','CRITICALINTERRUPTION'))");
                t.HasCheckConstraint("ck_incident_criticality", "(assessed_criticality IS NULL OR assessed_criticality IN ('NOTAPPLICABLE','LOW','MEDIUM','HIGH')) AND (criticality_assessed_at_revision IS NULL OR (criticality_assessed_at_revision > 1 AND criticality_assessed_at_revision <= revision)) AND (assessed_criticality IS NULL OR criticality_assessed_at_revision IS NOT NULL)");
                t.HasCheckConstraint("ck_incident_severity", "(severity IS NULL OR severity IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (maximum_severity_reached IS NULL OR maximum_severity_reached IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (severity IS NULL OR (maximum_severity_reached IS NOT NULL AND (CASE maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END) >= (CASE severity WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END))) AND ((severity_rule_version IS NULL AND severity IS NULL AND maximum_severity_reached IS NULL AND severity_assessed_at_revision IS NULL) OR (severity_rule_version IS NOT NULL AND severity_rule_version = 'openirp-severity-v1' AND severity_assessed_at_revision IS NOT NULL AND severity_assessed_at_revision > 0 AND severity_assessed_at_revision <= revision))");
                t.HasCheckConstraint("ck_incident_closure", "((closure_request_id IS NULL AND closure_request_revision IS NULL AND closure_requested_by_user_id IS NULL AND closure_review_status IS NULL AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_request_id IS NOT NULL AND closure_request_revision IS NOT NULL AND closure_request_revision > 1 AND closure_request_revision <= revision AND closure_requested_by_user_id IS NOT NULL AND closure_review_status IS NOT NULL AND closure_review_status IN ('PENDING','APPROVED','REJECTED','INVALIDATED') AND ((closure_review_status = 'PENDING' AND status = 'RESOLVED' AND closure_request_revision = revision AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_review_status <> 'PENDING' AND closure_decided_by_user_id IS NOT NULL AND closure_decision_revision IS NOT NULL AND closure_decision_revision > closure_request_revision AND closure_decision_revision <= revision AND closure_self_approval IS NOT NULL AND (closure_review_status <> 'APPROVED' OR status = 'CLOSED') AND ((closure_self_approval = TRUE AND closure_review_status = 'APPROVED' AND closure_decided_by_user_id = closure_requested_by_user_id AND closure_self_approval_justification IS NOT NULL AND btrim(closure_self_approval_justification) <> '') OR (closure_self_approval = FALSE AND closure_self_approval_justification IS NULL AND (closure_review_status <> 'APPROVED' OR closure_decided_by_user_id <> closure_requested_by_user_id)))))))");
                t.HasCheckConstraint("ck_incident_lifecycle", "(resolution IS NULL OR btrim(resolution) <> '') AND (status NOT IN ('RESOLVED','CLOSED') OR resolution IS NOT NULL) AND (status <> 'CLOSED' OR (severity IS NOT NULL AND maximum_severity_reached IS NOT NULL AND severity_rule_version IS NOT NULL AND assigned_membership_id IS NOT NULL AND (maximum_severity_reached IN ('LOW','MEDIUM') OR (maximum_severity_reached IN ('HIGH','CRITICAL') AND closure_review_status IS NOT NULL AND closure_review_status = 'APPROVED' AND closure_decision_revision = revision))))");
                t.HasCheckConstraint("ck_incident_status", "status IN ('REGISTERED','UNDER_EVALUATION','IN_RESPONSE','RESOLVED','CLOSED','DISCARDED')");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasAlternateKey(e => new { e.OrganizationId, e.Id });
            b.HasIndex(e => e.ClosureRequestId).IsUnique();
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.Property(e => e.Description).HasMaxLength(4000);
            b.Property(e => e.Status).HasMaxLength(30).IsRequired();
            b.Property(e => e.ConfidentialityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.IntegrityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AvailabilityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AssessedCriticality).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentCriticality>(v, true)).HasMaxLength(20);
            b.Property(e => e.Severity).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentSeverity>(v, true)).HasMaxLength(10);
            b.Property(e => e.MaximumSeverityReached).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentSeverity>(v, true)).HasMaxLength(10);
            b.Property(e => e.SeverityRuleVersion).HasMaxLength(64);
            b.Property(e => e.Resolution).HasMaxLength(4000);
            b.Property(e => e.ClosureSelfApprovalJustification).HasMaxLength(2000);
            b.Property(e => e.ClosureReviewStatus).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ClosureReviewStatus>(v, true)).HasMaxLength(20);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ClosureRequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ClosureDecidedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.Scope).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentScope>(v, true)).HasMaxLength(30);
            b.Property(e => e.OperationalImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<OperationalImpact>(v, true)).HasMaxLength(30);
            b.Property(e => e.Revision).IsConcurrencyToken();
            b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IncidentEvent>(b =>
        {
            b.ToTable("incident_events", "app", t =>
            {
                t.HasCheckConstraint("ck_incident_event_revision", "revision > 0");
                t.HasCheckConstraint("ck_incident_event_action", "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','CLOSURE_RISK_REVIEWED','OPERATIONAL_REVIEWED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
                t.HasCheckConstraint("ck_incident_event_details", "jsonb_typeof(details_json) = 'object' AND octet_length(details_json::text) <= 32768");
                t.HasCheckConstraint("ck_incident_event_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_incident_event_cia", "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");
                t.HasCheckConstraint("ck_incident_event_context", "(scope IS NULL OR scope IN ('SINGLE','MULTIPLE','ORGANIZATIONWIDE')) AND (operational_impact IS NULL OR operational_impact IN ('NONE','DEGRADED','MAJORINTERRUPTION','CRITICALINTERRUPTION'))");
                t.HasCheckConstraint("ck_incident_event_criticality", "(assessed_criticality IS NULL OR assessed_criticality IN ('NOTAPPLICABLE','LOW','MEDIUM','HIGH')) AND (criticality_assessed_at_revision IS NULL OR (criticality_assessed_at_revision > 1 AND criticality_assessed_at_revision <= revision)) AND (assessed_criticality IS NULL OR criticality_assessed_at_revision IS NOT NULL)");
                t.HasCheckConstraint("ck_incident_event_severity", "(severity IS NULL OR severity IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (maximum_severity_reached IS NULL OR maximum_severity_reached IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (severity IS NULL OR (maximum_severity_reached IS NOT NULL AND (CASE maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END) >= (CASE severity WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END))) AND ((severity_rule_version IS NULL AND severity IS NULL AND maximum_severity_reached IS NULL AND severity_assessed_at_revision IS NULL) OR (severity_rule_version IS NOT NULL AND severity_rule_version = 'openirp-severity-v1' AND severity_assessed_at_revision IS NOT NULL AND severity_assessed_at_revision > 0 AND severity_assessed_at_revision <= revision))");
                t.HasCheckConstraint("ck_incident_event_closure", "((closure_request_id IS NULL AND closure_request_revision IS NULL AND closure_requested_by_user_id IS NULL AND closure_review_status IS NULL AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_request_id IS NOT NULL AND closure_request_revision IS NOT NULL AND closure_request_revision > 1 AND closure_request_revision <= revision AND closure_requested_by_user_id IS NOT NULL AND closure_review_status IS NOT NULL AND closure_review_status IN ('PENDING','APPROVED','REJECTED','INVALIDATED') AND ((closure_review_status = 'PENDING' AND status = 'RESOLVED' AND closure_request_revision = revision AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_review_status <> 'PENDING' AND closure_decided_by_user_id IS NOT NULL AND closure_decision_revision IS NOT NULL AND closure_decision_revision > closure_request_revision AND closure_decision_revision <= revision AND closure_self_approval IS NOT NULL AND (closure_review_status <> 'APPROVED' OR status = 'CLOSED') AND ((closure_self_approval = TRUE AND closure_review_status = 'APPROVED' AND closure_decided_by_user_id = closure_requested_by_user_id AND closure_self_approval_justification IS NOT NULL AND btrim(closure_self_approval_justification) <> '') OR (closure_self_approval = FALSE AND closure_self_approval_justification IS NULL AND (closure_review_status <> 'APPROVED' OR closure_decided_by_user_id <> closure_requested_by_user_id)))))))");
                t.HasCheckConstraint("ck_incident_event_lifecycle", "(resolution IS NULL OR btrim(resolution) <> '') AND (status NOT IN ('RESOLVED','CLOSED') OR resolution IS NOT NULL) AND (status <> 'CLOSED' OR (severity IS NOT NULL AND maximum_severity_reached IS NOT NULL AND severity_rule_version IS NOT NULL AND assigned_membership_id IS NOT NULL AND (maximum_severity_reached IN ('LOW','MEDIUM') OR (maximum_severity_reached IN ('HIGH','CRITICAL') AND closure_review_status IS NOT NULL AND closure_review_status = 'APPROVED' AND closure_decision_revision = revision))))");
                t.HasCheckConstraint("ck_incident_event_status", "status IN ('REGISTERED','UNDER_EVALUATION','IN_RESPONSE','RESOLVED','CLOSED','DISCARDED')");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Title).HasMaxLength(200).IsRequired();
            b.Property(e => e.Description).HasMaxLength(4000);
            b.Property(e => e.Reason).HasMaxLength(2000);
            b.Property(e => e.Status).HasMaxLength(30).IsRequired();
            b.Property(e => e.ConfidentialityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.IntegrityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AvailabilityImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ImpactLevel>(v, true)).HasMaxLength(10);
            b.Property(e => e.AssessedCriticality).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentCriticality>(v, true)).HasMaxLength(20);
            b.Property(e => e.Severity).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentSeverity>(v, true)).HasMaxLength(10);
            b.Property(e => e.MaximumSeverityReached).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentSeverity>(v, true)).HasMaxLength(10);
            b.Property(e => e.SeverityRuleVersion).HasMaxLength(64);
            b.Property(e => e.Resolution).HasMaxLength(4000);
            b.Property(e => e.ClosureSelfApprovalJustification).HasMaxLength(2000);
            b.Property(e => e.ClosureReviewStatus).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<ClosureReviewStatus>(v, true)).HasMaxLength(20);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ClosureRequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ClosureDecidedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.Scope).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<IncidentScope>(v, true)).HasMaxLength(30);
            b.Property(e => e.OperationalImpact).HasConversion(v => v!.Value.ToString().ToUpperInvariant(), v => Enum.Parse<OperationalImpact>(v, true)).HasMaxLength(30);
            b.Property(e => e.Action).HasMaxLength(30).IsRequired();
            b.Property(e => e.DetailsJson).HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
            b.HasIndex(e => new { e.OrganizationId, e.IncidentId, e.Revision }).IsUnique();
            b.HasOne<Incident>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.IncidentId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrganizationMembership>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AssignedMembershipId })
                .HasPrincipalKey(e => new { e.OrganizationId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IncidentCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
