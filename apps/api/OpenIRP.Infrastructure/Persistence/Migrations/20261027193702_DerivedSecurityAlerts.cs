using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DerivedSecurityAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_execution_security_decisions_playbook_security_holds_versio",
                schema: "app",
                table: "execution_security_decisions");

            migrationBuilder.DropIndex(
                name: "ix_execution_security_decisions_version_id_security_hold_id",
                schema: "app",
                table: "execution_security_decisions");

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_security_hold_id",
                schema: "app",
                table: "execution_security_decisions",
                column: "security_hold_id");

            migrationBuilder.AddForeignKey(
                name: "fk_execution_security_decisions_playbook_security_holds_securi",
                schema: "app",
                table: "execution_security_decisions",
                column: "security_hold_id",
                principalSchema: "app",
                principalTable: "playbook_security_holds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
CREATE FUNCTION app.security_ancestors(ver uuid) RETURNS TABLE(id uuid) LANGUAGE sql STABLE SET search_path=pg_catalog,app AS $anc$
WITH RECURSIVE chain(id) AS(SELECT ver UNION SELECT src.id FROM chain c JOIN app.playbook_versions v ON v.id=c.id JOIN app.playbooks p ON p.id=v.playbook_id CROSS JOIN LATERAL unnest(ARRAY[v.source_version_id,p.source_version_id]) src(id) WHERE src.id IS NOT NULL) SELECT id FROM chain;
$anc$;
CREATE FUNCTION app.effective_security_hold(ver uuid,at_time timestamptz DEFAULT NULL) RETURNS uuid LANGUAGE sql STABLE SET search_path=pg_catalog,app AS $hold$
SELECT h.id FROM app.playbook_security_holds h WHERE h.version_id IN(SELECT id FROM app.security_ancestors(ver)) AND (at_time IS NULL OR h.occurred_at<=at_time) ORDER BY h.occurred_at DESC,h.revision DESC,h.id DESC LIMIT 1;
$hold$;
CREATE OR REPLACE FUNCTION app.execution_version_usable(run uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog,app AS $usable$
SELECT EXISTS(SELECT 1 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
LEFT JOIN LATERAL(SELECT app.effective_security_hold(e.version_id,NULL) id) h ON true
WHERE e.id=run AND v.status IN('PUBLISHED','ARCHIVED') AND
((h.id IS NULL AND v.status='PUBLISHED') OR (h.id IS NOT NULL AND EXISTS(SELECT 1 FROM app.execution_security_decisions d WHERE d.organization_id=e.organization_id AND d.incident_id=e.incident_id AND d.execution_id=e.id AND d.security_hold_id=h.id AND d.action='CONTINUE') AND NOT EXISTS(SELECT 1 FROM app.execution_security_decisions d WHERE d.execution_id=e.id AND d.security_hold_id=h.id AND d.action='CANCEL'))));
$usable$;
CREATE FUNCTION app.check_inherited_start() RETURNS trigger LANGUAGE plpgsql AS $start$
BEGIN IF app.effective_security_hold(NEW.version_id,NULL) IS NOT NULL THEN RAISE EXCEPTION 'Security held source cannot start new executions' USING ERRCODE='23514'; END IF; RETURN NEW; END;$start$;
CREATE TRIGGER check_inherited_start BEFORE INSERT ON app.playbook_executions FOR EACH ROW EXECUTE FUNCTION app.check_inherited_start();
CREATE OR REPLACE FUNCTION app.check_security_decision() RETURNS trigger LANGUAGE plpgsql AS $b$
                DECLARE run app.playbook_executions%ROWTYPE; parent app.incidents%ROWTYPE; expected_action text;
                BEGIN
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR run.revision<>NEW.execution_revision OR run.status<>(CASE WHEN NEW.action='CONTINUE' THEN 'IN_PROGRESS' ELSE 'CANCELLED' END)
                 OR NEW.security_hold_id IS DISTINCT FROM app.effective_security_hold(NEW.version_id,NULL) THEN
                 RAISE EXCEPTION 'Security decision requires current hold and execution' USING ERRCODE='23514'; END IF;
                 SELECT * INTO parent FROM app.incidents WHERE id=NEW.incident_id AND organization_id=NEW.organization_id;
                 IF NOT FOUND OR parent.revision<>NEW.incident_revision OR parent.status<>'IN_RESPONSE'
                 OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN app.organizations o ON o.id=m.organization_id JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN' AND o.status='ACTIVE'
                 AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                 RAISE EXCEPTION 'Security decision requires eligible organization admin' USING ERRCODE='23514'; END IF;
                 expected_action=CASE WHEN NEW.action='CONTINUE' THEN 'SECURITY_CONTINUITY_AUTHORIZED' ELSE 'SECURITY_EXECUTION_CANCELLED' END;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.execution_id=NEW.execution_id AND h.organization_id=NEW.organization_id
                 AND h.incident_id=NEW.incident_id AND h.version_id=NEW.version_id AND h.revision=NEW.execution_revision AND h.incident_revision=NEW.incident_revision
                 AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action=expected_action AND h.reason=NEW.reason AND h.task_id IS NULL AND h.execution_status=run.status)
                 OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id AND h.revision=NEW.incident_revision
                 AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action=expected_action AND h.reason=NEW.reason) THEN
                 RAISE EXCEPTION 'Security decision requires matching atomic history' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END;$b$;
CREATE OR REPLACE FUNCTION app.check_security_execution() RETURNS trigger LANGUAGE plpgsql AS $b$
                DECLARE run app.playbook_executions%ROWTYPE; hold uuid;
                BEGIN
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.id;
                 IF run.status='COMPLETED' AND NOT app.execution_version_usable(run.id) THEN RAISE EXCEPTION 'Held or withdrawn execution cannot complete' USING ERRCODE='23514'; END IF;
                 IF run.status='CANCELLED' THEN
                 hold := app.effective_security_hold(run.version_id,NULL);
                 IF hold IS NOT NULL AND NOT EXISTS(SELECT 1 FROM app.execution_security_decisions WHERE execution_id=run.id AND security_hold_id=hold AND action='CANCEL' AND execution_revision=run.revision) THEN
                 RAISE EXCEPTION 'Cancellation requires decision for current security hold' USING ERRCODE='23514'; END IF;
                 END IF;
                 RETURN NEW;
                END;$b$;
CREATE OR REPLACE FUNCTION app.operational_context(org uuid,inc uuid) RETURNS text
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
'holds',(SELECT coalesce(jsonb_agg(jsonb_build_array(h.id,h.version_id,h.revision) ORDER BY h.id),'[]'::jsonb) FROM app.playbook_security_holds h WHERE h.version_id IN (SELECT a.id FROM runs r CROSS JOIN LATERAL app.security_ancestors(r.version_id) a)),
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
'holds',(SELECT coalesce(jsonb_agg(jsonb_build_array(h.id,h.version_id,h.revision) ORDER BY h.id),'[]'::jsonb) FROM app.playbook_security_holds h WHERE h.version_id IN (SELECT a.id FROM runs r CROSS JOIN LATERAL app.security_ancestors(r.version_id) a)),
'organization-capabilities',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.id,p.capability_id,p.revision) ORDER BY p.id),'[]'::jsonb) FROM app.organization_capabilities p WHERE p.organization_id=org),
'capability-catalog',(SELECT coalesce(jsonb_agg(jsonb_build_array(c.id,c.revision,c.is_active) ORDER BY c.id),'[]'::jsonb) FROM app.capability_catalog c WHERE EXISTS(SELECT 1 FROM app.organization_capabilities p WHERE p.organization_id=org AND p.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.incident_capability_assessments x WHERE x.organization_id=org AND x.incident_id=inc AND x.capability_id=c.id) OR EXISTS(SELECT 1 FROM app.playbook_capability_requirements q WHERE q.capability_id=c.id AND q.version_id IN (SELECT version_id FROM runs)))
) AS value)
SELECT 'risk-v1:'||encode(sha256(convert_to(value::text,'UTF8')),'hex') FROM payload
WHERE EXISTS(SELECT 1 FROM app.incidents WHERE organization_id=org AND id=inc);
$context$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $down$ BEGIN IF EXISTS(SELECT 1 FROM app.execution_security_decisions d JOIN app.playbook_security_holds h ON h.id=d.security_hold_id WHERE h.version_id<>d.version_id) OR (EXISTS(SELECT 1 FROM app.playbook_security_holds) AND EXISTS(SELECT 1 FROM app.playbooks WHERE source_version_id IS NOT NULL)) THEN RAISE EXCEPTION 'Cannot remove inherited security protection with affected history'; END IF; END;$down$;
DROP TRIGGER check_inherited_start ON app.playbook_executions;
DROP FUNCTION app.check_inherited_start();
CREATE OR REPLACE FUNCTION app.execution_version_usable(run uuid) RETURNS boolean LANGUAGE sql STABLE AS $b$
                 SELECT EXISTS(SELECT 1 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                 WHERE e.id=run AND (v.status='PUBLISHED' OR (v.status='ARCHIVED' AND EXISTS(
                 SELECT 1 FROM app.playbook_security_holds h JOIN app.execution_security_decisions d ON d.security_hold_id=h.id
                  AND d.execution_id=e.id AND d.organization_id=e.organization_id AND d.incident_id=e.incident_id AND d.action='CONTINUE'
                 WHERE h.version_id=e.version_id AND h.id=(SELECT id FROM app.playbook_security_holds WHERE version_id=e.version_id ORDER BY revision DESC LIMIT 1)
                 AND NOT EXISTS(SELECT 1 FROM app.execution_security_decisions c WHERE c.execution_id=e.id AND c.security_hold_id=h.id AND c.action='CANCEL')))));
                $b$;
CREATE OR REPLACE FUNCTION app.check_security_decision() RETURNS trigger LANGUAGE plpgsql AS $b$
                DECLARE run app.playbook_executions%ROWTYPE; parent app.incidents%ROWTYPE; expected_action text;
                BEGIN
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR run.revision<>NEW.execution_revision OR run.status<>(CASE WHEN NEW.action='CONTINUE' THEN 'IN_PROGRESS' ELSE 'CANCELLED' END)
                 OR NEW.security_hold_id IS DISTINCT FROM (SELECT id FROM app.playbook_security_holds WHERE version_id=NEW.version_id ORDER BY revision DESC LIMIT 1) THEN
                 RAISE EXCEPTION 'Security decision requires current hold and execution' USING ERRCODE='23514'; END IF;
                 SELECT * INTO parent FROM app.incidents WHERE id=NEW.incident_id AND organization_id=NEW.organization_id;
                 IF NOT FOUND OR parent.revision<>NEW.incident_revision OR parent.status<>'IN_RESPONSE'
                 OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN app.organizations o ON o.id=m.organization_id JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN' AND o.status='ACTIVE'
                 AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                 RAISE EXCEPTION 'Security decision requires eligible organization admin' USING ERRCODE='23514'; END IF;
                 expected_action=CASE WHEN NEW.action='CONTINUE' THEN 'SECURITY_CONTINUITY_AUTHORIZED' ELSE 'SECURITY_EXECUTION_CANCELLED' END;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.execution_id=NEW.execution_id AND h.organization_id=NEW.organization_id
                 AND h.incident_id=NEW.incident_id AND h.version_id=NEW.version_id AND h.revision=NEW.execution_revision AND h.incident_revision=NEW.incident_revision
                 AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action=expected_action AND h.reason=NEW.reason AND h.task_id IS NULL AND h.execution_status=run.status)
                 OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id AND h.revision=NEW.incident_revision
                 AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action=expected_action AND h.reason=NEW.reason) THEN
                 RAISE EXCEPTION 'Security decision requires matching atomic history' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END;$b$;
CREATE OR REPLACE FUNCTION app.check_security_execution() RETURNS trigger LANGUAGE plpgsql AS $b$
                DECLARE run app.playbook_executions%ROWTYPE; hold uuid;
                BEGIN
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.id;
                 IF run.status='COMPLETED' AND NOT app.execution_version_usable(run.id) THEN RAISE EXCEPTION 'Held or withdrawn execution cannot complete' USING ERRCODE='23514'; END IF;
                 IF run.status='CANCELLED' THEN
                 SELECT id INTO hold FROM app.playbook_security_holds WHERE version_id=run.version_id ORDER BY revision DESC LIMIT 1;
                 IF hold IS NOT NULL AND NOT EXISTS(SELECT 1 FROM app.execution_security_decisions WHERE execution_id=run.id AND security_hold_id=hold AND action='CANCEL' AND execution_revision=run.revision) THEN
                 RAISE EXCEPTION 'Cancellation requires decision for current security hold' USING ERRCODE='23514'; END IF;
                 END IF;
                 RETURN NEW;
                END;$b$;
CREATE OR REPLACE FUNCTION app.operational_context(org uuid,inc uuid) RETURNS text
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
DROP FUNCTION app.effective_security_hold(uuid,timestamptz);
DROP FUNCTION app.security_ancestors(uuid);

""");
            migrationBuilder.DropForeignKey(
                name: "fk_execution_security_decisions_playbook_security_holds_securi",
                schema: "app",
                table: "execution_security_decisions");

            migrationBuilder.DropIndex(
                name: "ix_execution_security_decisions_security_hold_id",
                schema: "app",
                table: "execution_security_decisions");

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_version_id_security_hold_id",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "version_id", "security_hold_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_execution_security_decisions_playbook_security_holds_versio",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "version_id", "security_hold_id" },
                principalSchema: "app",
                principalTable: "playbook_security_holds",
                principalColumns: new[] { "version_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
