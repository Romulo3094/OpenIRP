using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_execution_tasks_organization_id_incident_id_id",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "organization_id", "incident_id", "id" });

            migrationBuilder.CreateTable(
                name: "operational_reviews",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accepted = table.Column<bool>(type: "boolean", nullable: false),
                    method = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    assessment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    alternative_procedure = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    context_token = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    self_review = table.Column<bool>(type: "boolean", nullable: false),
                    self_review_justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operational_reviews", x => x.id);
                    table.CheckConstraint("ck_operational_review_alternative", "(kind='EVIDENCE' AND alternative_procedure IS NULL) OR (kind='ALTERNATIVE' AND alternative_procedure IS NOT NULL AND btrim(alternative_procedure)<>'')");
                    table.CheckConstraint("ck_operational_review_self", "(self_review AND self_review_justification IS NOT NULL AND btrim(self_review_justification)<>'') OR (NOT self_review AND self_review_justification IS NULL)");
                    table.CheckConstraint("ck_operational_review_values", "kind IN ('EVIDENCE','ALTERNATIVE') AND btrim(method)<>'' AND btrim(assessment)<>'' AND btrim(reason)<>'' AND incident_revision>1 AND context_token ~ '^ops-v1:[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_operational_reviews_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operational_reviews_execution_tasks_organization_id_inciden",
                        columns: x => new { x.organization_id, x.incident_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operational_reviews_incident_evidence_organization_id_incid",
                        columns: x => new { x.organization_id, x.incident_id, x.evidence_id },
                        principalSchema: "app",
                        principalTable: "incident_evidence",
                        principalColumns: new[] { "organization_id", "incident_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','CLOSURE_RISK_REVIEWED','OPERATIONAL_REVIEWED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.CreateIndex(
                name: "ix_operational_reviews_actor_user_id",
                schema: "app",
                table: "operational_reviews",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_operational_reviews_execution_id_task_id_kind_incident_revi",
                schema: "app",
                table: "operational_reviews",
                columns: new[] { "execution_id", "task_id", "kind", "incident_revision" });

            migrationBuilder.CreateIndex(
                name: "ix_operational_reviews_organization_id_incident_id_evidence_id",
                schema: "app",
                table: "operational_reviews",
                columns: new[] { "organization_id", "incident_id", "evidence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operational_reviews_organization_id_incident_id_incident_re",
                schema: "app",
                table: "operational_reviews",
                columns: new[] { "organization_id", "incident_id", "incident_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operational_reviews_organization_id_incident_id_task_id",
                schema: "app",
                table: "operational_reviews",
                columns: new[] { "organization_id", "incident_id", "task_id" });
            migrationBuilder.Sql("""
CREATE FUNCTION app.operational_context(org uuid,inc uuid) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,app SET timezone='UTC' AS $context$
WITH runs AS(SELECT id,version_id FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
payload AS(SELECT jsonb_build_object(
'protocol','ops-v1',
'incident',(SELECT to_jsonb(i)-ARRAY['revision','status','closure_request_id','closure_request_revision','closure_requested_by_user_id','closure_review_status','closure_decided_by_user_id','closure_decision_revision','closure_self_approval','closure_self_approval_justification','severity_assessed_at_revision']::text[] FROM app.incidents i WHERE i.organization_id=org AND i.id=inc),
'semantic-revision',(SELECT coalesce(max(e.revision),0) FROM app.incident_events e WHERE e.organization_id=org AND e.incident_id=inc AND e.action NOT IN ('OPERATIONAL_REVIEWED','CLOSURE_RISK_REVIEWED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED') AND NOT(e.action='STATE_CHANGED' AND e.status='CLOSED')),
'organization',(SELECT to_jsonb(o) FROM app.organizations o WHERE o.id=org),
'members',(SELECT coalesce(jsonb_agg(jsonb_build_array(m.id,m.user_id,m.role,m.status,m.updated_at,u.is_active,u.lockout_end,(u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp())) ORDER BY m.id),'[]'::jsonb) FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id WHERE m.organization_id=org),
'category',(SELECT jsonb_build_array(c.id,c.revision,c.is_active) FROM app.incident_categories c JOIN app.incidents i ON i.category_id=c.id WHERE i.organization_id=org AND i.id=inc),
'executions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,version_id,revision,status) ORDER BY id),'[]'::jsonb) FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
'tasks',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,execution_id,status,assigned_membership_id,completion_evidence_id,changed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.execution_tasks WHERE organization_id=org AND incident_id=inc),
'security-decisions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.execution_security_decisions WHERE organization_id=org AND incident_id=inc),
'evidence',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,revision,status,execution_id,task_id,kind,classification) ORDER BY id),'[]'::jsonb) FROM app.incident_evidence WHERE organization_id=org AND incident_id=inc),
'files',(SELECT coalesce(jsonb_agg(jsonb_build_array(evidence_id,revision,state,length,sha256) ORDER BY evidence_id),'[]'::jsonb) FROM app.evidence_files WHERE organization_id=org AND incident_id=inc),
'step-approvals',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.step_approvals WHERE organization_id=org AND incident_id=inc),
'skips',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.skip_authorizations WHERE organization_id=org AND incident_id=inc),
'dependency-exceptions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exceptions WHERE organization_id=org AND incident_id=inc),
'dependency-revocations',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exception_revocations WHERE organization_id=org AND incident_id=inc),
'incident-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,capability_id,assessed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.incident_capability_assessments WHERE organization_id=org AND incident_id=inc),
'versions',(SELECT coalesce(jsonb_agg(jsonb_build_array(v.id,v.status) ORDER BY v.id),'[]'::jsonb) FROM app.playbook_versions v WHERE v.id IN (SELECT version_id FROM runs)),
'holds',(SELECT coalesce(jsonb_agg(jsonb_build_array(h.id,h.version_id,h.revision) ORDER BY h.id),'[]'::jsonb) FROM app.playbook_security_holds h WHERE h.version_id IN (SELECT version_id FROM runs)),
'organization-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.id,p.capability_id,p.revision) ORDER BY p.id),'[]'::jsonb) FROM app.organization_capabilities p WHERE p.organization_id=org),
'capability-catalog',(SELECT coalesce(jsonb_agg(jsonb_build_array(c.id,c.revision,c.is_active) ORDER BY c.id),'[]'::jsonb) FROM app.capability_catalog c WHERE EXISTS(SELECT 1 FROM app.organization_capabilities p WHERE p.organization_id=org AND p.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.incident_capability_assessments x WHERE x.organization_id=org AND x.incident_id=inc AND x.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.playbook_capability_requirements q WHERE q.capability_id=c.id AND q.version_id IN (SELECT version_id FROM runs)))
) AS value)
SELECT 'ops-v1:'||encode(sha256(convert_to(value::text,'UTF8')),'hex') FROM payload
WHERE EXISTS(SELECT 1 FROM app.incidents WHERE organization_id=org AND id=inc);
$context$;
CREATE OR REPLACE FUNCTION app.closure_risk_context(org uuid,inc uuid) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,app SET timezone='UTC' AS $context$
WITH runs AS(SELECT id,version_id FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
payload AS(SELECT jsonb_build_object(
'protocol','risk-v1',
'incident',(SELECT to_jsonb(i)-ARRAY['revision','status','closure_request_id','closure_request_revision','closure_requested_by_user_id','closure_review_status','closure_decided_by_user_id','closure_decision_revision','closure_self_approval','closure_self_approval_justification','severity_assessed_at_revision']::text[] FROM app.incidents i WHERE i.organization_id=org AND i.id=inc),
'semantic-revision',(SELECT coalesce(max(e.revision),0) FROM app.incident_events e WHERE e.organization_id=org AND e.incident_id=inc AND e.action NOT IN ('CLOSURE_RISK_REVIEWED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED') AND NOT(e.action='STATE_CHANGED' AND e.status='CLOSED')),
'operational-reviews',(SELECT coalesce(jsonb_agg(id ORDER BY id),'[]'::jsonb) FROM app.operational_reviews WHERE organization_id=org AND incident_id=inc),
'organization',(SELECT to_jsonb(o) FROM app.organizations o WHERE o.id=org),
'members',(SELECT coalesce(jsonb_agg(jsonb_build_array(m.id,m.user_id,m.role,m.status,m.updated_at,u.is_active,u.lockout_end,(u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp())) ORDER BY m.id),'[]'::jsonb) FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id WHERE m.organization_id=org),
'category',(SELECT jsonb_build_array(c.id,c.revision,c.is_active) FROM app.incident_categories c JOIN app.incidents i ON i.category_id=c.id WHERE i.organization_id=org AND i.id=inc),
'executions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,version_id,revision,status) ORDER BY id),'[]'::jsonb) FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
'tasks',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,execution_id,status,assigned_membership_id,completion_evidence_id,changed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.execution_tasks WHERE organization_id=org AND incident_id=inc),
'security-decisions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.execution_security_decisions WHERE organization_id=org AND incident_id=inc),
'evidence',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,revision,status,execution_id,task_id,kind,classification) ORDER BY id),'[]'::jsonb) FROM app.incident_evidence WHERE organization_id=org AND incident_id=inc),
'files',(SELECT coalesce(jsonb_agg(jsonb_build_array(evidence_id,revision,state,length,sha256) ORDER BY evidence_id),'[]'::jsonb) FROM app.evidence_files WHERE organization_id=org AND incident_id=inc),
'step-approvals',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.step_approvals WHERE organization_id=org AND incident_id=inc),
'skips',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.skip_authorizations WHERE organization_id=org AND incident_id=inc),
'dependency-exceptions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exceptions WHERE organization_id=org AND incident_id=inc),
'dependency-revocations',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exception_revocations WHERE organization_id=org AND incident_id=inc),
'incident-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,capability_id,assessed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.incident_capability_assessments WHERE organization_id=org AND incident_id=inc),
'versions',(SELECT coalesce(jsonb_agg(jsonb_build_array(v.id,v.status) ORDER BY v.id),'[]'::jsonb) FROM app.playbook_versions v WHERE v.id IN (SELECT version_id FROM runs)),
'holds',(SELECT coalesce(jsonb_agg(jsonb_build_array(h.id,h.version_id,h.revision) ORDER BY h.id),'[]'::jsonb) FROM app.playbook_security_holds h WHERE h.version_id IN (SELECT version_id FROM runs)),
'organization-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.id,p.capability_id,p.revision) ORDER BY p.id),'[]'::jsonb) FROM app.organization_capabilities p WHERE p.organization_id=org),
'capability-catalog',(SELECT coalesce(jsonb_agg(jsonb_build_array(c.id,c.revision,c.is_active) ORDER BY c.id),'[]'::jsonb) FROM app.capability_catalog c WHERE EXISTS(SELECT 1 FROM app.organization_capabilities p WHERE p.organization_id=org AND p.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.incident_capability_assessments x WHERE x.organization_id=org AND x.incident_id=inc AND x.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.playbook_capability_requirements q WHERE q.capability_id=c.id AND q.version_id IN (SELECT version_id FROM runs)))
) AS value)
SELECT 'risk-v1:'||encode(sha256(convert_to(value::text,'UTF8')),'hex') FROM payload
WHERE EXISTS(SELECT 1 FROM app.incidents WHERE organization_id=org AND id=inc);
$context$;
CREATE FUNCTION app.protect_operational_review() RETURNS trigger LANGUAGE plpgsql AS $guard$
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Operational reviews are append-only' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(1869636978,26); RETURN NEW;
END;$guard$;
CREATE TRIGGER protect_operational_review BEFORE INSERT OR UPDATE OR DELETE ON app.operational_reviews FOR EACH ROW EXECUTE FUNCTION app.protect_operational_review();
CREATE TRIGGER truncate_operational_review BEFORE TRUNCATE ON app.operational_reviews FOR EACH STATEMENT EXECUTE FUNCTION app.protect_operational_review();
CREATE FUNCTION app.check_operational_review() RETURNS trigger LANGUAGE plpgsql AS $guard$
DECLARE parent app.incidents; task app.execution_tasks; evidence app.incident_evidence; own boolean;
BEGIN
 SELECT * INTO parent FROM app.incidents WHERE organization_id=NEW.organization_id AND id=NEW.incident_id;
 SELECT * INTO task FROM app.execution_tasks WHERE organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND execution_id=NEW.execution_id AND id=NEW.task_id;
 SELECT * INTO evidence FROM app.incident_evidence WHERE organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND id=NEW.evidence_id;
 IF parent.id IS NULL OR parent.status IN ('CLOSED','DISCARDED') OR parent.revision<>NEW.incident_revision OR task.id IS NULL
 OR evidence.id IS NULL OR evidence.status<>'ACTIVE' OR evidence.task_id IS DISTINCT FROM task.id OR evidence.execution_id IS DISTINCT FROM NEW.execution_id OR evidence.version_id IS DISTINCT FROM task.version_id
 OR (NEW.kind='EVIDENCE' AND (task.status<>'COMPLETED' OR task.completion_evidence_id IS DISTINCT FROM evidence.id))
 OR (NEW.accepted AND evidence.kind='FILE' AND NOT EXISTS(SELECT 1 FROM app.evidence_files WHERE evidence_id=evidence.id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND state='AVAILABLE'))
 OR NEW.context_token IS DISTINCT FROM app.operational_context(NEW.organization_id,NEW.incident_id)
 OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id JOIN app.organizations o ON o.id=m.organization_id
 WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.role='ORG_ADMIN' AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp()))
 OR NOT EXISTS(SELECT 1 FROM app.incident_events e WHERE e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.revision=NEW.incident_revision AND e.action='OPERATIONAL_REVIEWED'
 AND e.actor_user_id=NEW.actor_user_id AND e.correlation_id=NEW.correlation_id AND e.reason=NEW.reason AND e.details_json->>'operationalReviewId'=NEW.id::text)
 OR NOT EXISTS(SELECT 1 FROM audit.audit_events a WHERE a.organization_id=NEW.organization_id AND a.resource_id=NEW.incident_id AND a.actor_user_id=NEW.actor_user_id AND a.correlation_id=NEW.correlation_id AND a.action='incident.operational_reviewed' AND a.outcome='SUCCESS')
 THEN RAISE EXCEPTION 'Invalid operational review context or atomic history' USING ERRCODE='23514'; END IF;
 SELECT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.id IN(task.assigned_membership_id,parent.assigned_membership_id)) INTO own;
 IF own IS DISTINCT FROM NEW.self_review THEN RAISE EXCEPTION 'Invalid operational self-review' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END;$guard$;
CREATE CONSTRAINT TRIGGER check_operational_review AFTER INSERT ON app.operational_reviews DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_operational_review();

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $down$ BEGIN IF EXISTS(SELECT 1 FROM app.operational_reviews) THEN RAISE EXCEPTION 'Cannot remove recorded operational reviews'; END IF; END;$down$;
DROP TRIGGER check_operational_review ON app.operational_reviews;
DROP TRIGGER protect_operational_review ON app.operational_reviews;
DROP TRIGGER truncate_operational_review ON app.operational_reviews;
DROP FUNCTION app.check_operational_review();
DROP FUNCTION app.protect_operational_review();
DROP FUNCTION app.operational_context(uuid,uuid);
CREATE OR REPLACE FUNCTION app.closure_risk_context(org uuid,inc uuid) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,app SET timezone='UTC' AS $context$
WITH runs AS(SELECT id,version_id FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
payload AS(SELECT jsonb_build_object(
'protocol','risk-v1',
'incident',(SELECT to_jsonb(i)-ARRAY['revision','status','closure_request_id','closure_request_revision','closure_requested_by_user_id','closure_review_status','closure_decided_by_user_id','closure_decision_revision','closure_self_approval','closure_self_approval_justification','severity_assessed_at_revision']::text[] FROM app.incidents i WHERE i.organization_id=org AND i.id=inc),
'semantic-revision',(SELECT coalesce(max(e.revision),0) FROM app.incident_events e WHERE e.organization_id=org AND e.incident_id=inc AND e.action NOT IN ('CLOSURE_RISK_REVIEWED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED') AND NOT(e.action='STATE_CHANGED' AND e.status='CLOSED')),
'organization',(SELECT to_jsonb(o) FROM app.organizations o WHERE o.id=org),
'members',(SELECT coalesce(jsonb_agg(jsonb_build_array(m.id,m.user_id,m.role,m.status,m.updated_at,u.is_active,u.lockout_end,(u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp())) ORDER BY m.id),'[]'::jsonb) FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id WHERE m.organization_id=org),
'category',(SELECT jsonb_build_array(c.id,c.revision,c.is_active) FROM app.incident_categories c JOIN app.incidents i ON i.category_id=c.id WHERE i.organization_id=org AND i.id=inc),
'executions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,version_id,revision,status) ORDER BY id),'[]'::jsonb) FROM app.playbook_executions WHERE organization_id=org AND incident_id=inc),
'tasks',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,execution_id,status,assigned_membership_id,completion_evidence_id,changed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.execution_tasks WHERE organization_id=org AND incident_id=inc),
'security-decisions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.execution_security_decisions WHERE organization_id=org AND incident_id=inc),
'evidence',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,revision,status,execution_id,task_id,kind,classification) ORDER BY id),'[]'::jsonb) FROM app.incident_evidence WHERE organization_id=org AND incident_id=inc),
'files',(SELECT coalesce(jsonb_agg(jsonb_build_array(evidence_id,revision,state,length,sha256) ORDER BY evidence_id),'[]'::jsonb) FROM app.evidence_files WHERE organization_id=org AND incident_id=inc),
'step-approvals',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.step_approvals WHERE organization_id=org AND incident_id=inc),
'skips',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.skip_authorizations WHERE organization_id=org AND incident_id=inc),
'dependency-exceptions',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exceptions WHERE organization_id=org AND incident_id=inc),
'dependency-revocations',(SELECT coalesce(jsonb_agg(jsonb_build_array(id) ORDER BY id),'[]'::jsonb) FROM app.dependency_exception_revocations WHERE organization_id=org AND incident_id=inc),
'incident-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(id,capability_id,assessed_at_revision) ORDER BY id),'[]'::jsonb) FROM app.incident_capability_assessments WHERE organization_id=org AND incident_id=inc),
'versions',(SELECT coalesce(jsonb_agg(jsonb_build_array(v.id,v.status) ORDER BY v.id),'[]'::jsonb) FROM app.playbook_versions v WHERE v.id IN (SELECT version_id FROM runs)),
'holds',(SELECT coalesce(jsonb_agg(jsonb_build_array(h.id,h.version_id,h.revision) ORDER BY h.id),'[]'::jsonb) FROM app.playbook_security_holds h WHERE h.version_id IN (SELECT version_id FROM runs)),
'organization-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.id,p.capability_id,p.revision) ORDER BY p.id),'[]'::jsonb) FROM app.organization_capabilities p WHERE p.organization_id=org),
'capability-catalog',(SELECT coalesce(jsonb_agg(jsonb_build_array(c.id,c.revision,c.is_active) ORDER BY c.id),'[]'::jsonb) FROM app.capability_catalog c WHERE EXISTS(SELECT 1 FROM app.organization_capabilities p WHERE p.organization_id=org AND p.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.incident_capability_assessments x WHERE x.organization_id=org AND x.incident_id=inc AND x.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.playbook_capability_requirements q WHERE q.capability_id=c.id AND q.version_id IN (SELECT version_id FROM runs)))
) AS value)
SELECT 'risk-v1:'||encode(sha256(convert_to(value::text,'UTF8')),'hex') FROM payload
WHERE EXISTS(SELECT 1 FROM app.incidents WHERE organization_id=org AND id=inc);
$context$;
""");
            migrationBuilder.DropTable(
                name: "operational_reviews",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_execution_tasks_organization_id_incident_id_id",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','CLOSURE_RISK_REVIEWED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
