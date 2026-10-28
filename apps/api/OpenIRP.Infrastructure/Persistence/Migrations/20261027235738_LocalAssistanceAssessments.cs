using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalAssistanceAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistance_assessments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    execution_revision = table.Column<long>(type: "bigint", nullable: false),
                    context_token = table.Column<string>(type: "character varying(72)", maxLength: 72, nullable: false),
                    context_json = table.Column<string>(type: "text", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    parameters_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    recommendation_json = table.Column<string>(type: "text", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistance_assessments", x => x.id);
                    table.CheckConstraint("ck_assistance_context", "jsonb_typeof(context_json::jsonb)='object' AND octet_length(context_json)<=32768 AND input_hash=encode(sha256(convert_to(context_json,'UTF8')),'hex')");
                    table.CheckConstraint("ck_assistance_outcome", "(outcome='VALID' AND error_code IS NULL AND recommendation_json IS NOT NULL AND jsonb_typeof(recommendation_json::jsonb)='object' AND octet_length(recommendation_json)<=16384) OR (outcome IN ('INVALID','UNAVAILABLE','STALE') AND error_code IS NOT NULL AND recommendation_json IS NULL)");
                    table.CheckConstraint("ck_assistance_values", "kind='EXPLAIN_STEP' AND schema_version='openirp-assistance-v1' AND prompt_version='openirp-explain-step-v1' AND incident_revision>0 AND execution_revision>0 AND duration_ms BETWEEN 0 AND 600000 AND context_token ~ '^risk-v1:[0-9a-f]{64}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (model_digest IS NULL OR model_digest ~ '^[0-9a-f]{64}$')");
                    table.ForeignKey(
                        name: "fk_assistance_assessments_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assistance_assessments_execution_tasks_organization_id_inci",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistance_assessments_actor_user_id",
                schema: "app",
                table: "assistance_assessments",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistance_assessments_organization_id_incident_id_executio",
                schema: "app",
                table: "assistance_assessments",
                columns: new[] { "organization_id", "incident_id", "execution_id", "task_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_assistance_assessments_organization_id_incident_id_version_",
                schema: "app",
                table: "assistance_assessments",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id" });
            migrationBuilder.Sql("""
CREATE FUNCTION app.protect_assistance_history() RETURNS trigger LANGUAGE plpgsql AS $guard$
BEGIN IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'AI assessments are append-only' USING ERRCODE='42501'; END IF;
PERFORM pg_advisory_xact_lock(1869636978,26); RETURN NEW; END;$guard$;
CREATE TRIGGER protect_assistance_history BEFORE INSERT OR UPDATE OR DELETE ON app.assistance_assessments FOR EACH ROW EXECUTE FUNCTION app.protect_assistance_history();
CREATE TRIGGER truncate_assistance_history BEFORE TRUNCATE ON app.assistance_assessments FOR EACH STATEMENT EXECUTE FUNCTION app.protect_assistance_history();
CREATE FUNCTION app.check_assistance_assessment() RETURNS trigger LANGUAGE plpgsql AS $check$
DECLARE parent app.incidents; run app.playbook_executions; task app.execution_tasks; step app.playbook_steps; input jsonb; recommendation jsonb;
BEGIN
 SELECT * INTO parent FROM app.incidents WHERE organization_id=NEW.organization_id AND id=NEW.incident_id;
 SELECT * INTO run FROM app.playbook_executions WHERE organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND id=NEW.execution_id AND version_id=NEW.version_id;
 SELECT * INTO task FROM app.execution_tasks WHERE organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND execution_id=NEW.execution_id AND version_id=NEW.version_id AND id=NEW.task_id;
 SELECT * INTO step FROM app.playbook_steps WHERE id=task.step_id AND version_id=NEW.version_id;
 input:=NEW.context_json::jsonb;
 IF parent.id IS NULL OR run.id IS NULL OR task.id IS NULL OR step.id IS NULL OR NEW.incident_revision>parent.revision OR NEW.execution_revision>run.revision
 OR btrim(NEW.reason)='' OR NEW.parameters_json::jsonb<>'{"temperature":0,"num_predict":384,"num_ctx":4096,"stream":false,"think":false}'::jsonb
 OR NEW.model NOT IN('qwen3:4b-instruct','unconfigured')
 OR (SELECT count(*) FROM jsonb_object_keys(input))<>19
 OR NOT ARRAY(SELECT jsonb_object_keys(input))<@ARRAY['schemaVersion','category','severity','maximumSeverity','confidentiality','integrity','availability','scope','operationalImpact','criticality','stepCode','phase','instructions','taskStatus','isRequired','requiresApproval','requiresEvidence','capabilities','predecessors']::text[]
 OR input->>'schemaVersion' IS DISTINCT FROM NEW.schema_version OR input->>'stepCode' IS DISTINCT FROM step.code OR input->>'instructions' IS DISTINCT FROM step.instructions
 OR (input->>'isRequired')::boolean IS DISTINCT FROM step.is_required OR (input->>'requiresApproval')::boolean IS DISTINCT FROM step.requires_approval OR (input->>'requiresEvidence')::boolean IS DISTINCT FROM step.requires_evidence
 OR NOT EXISTS(SELECT 1 FROM app.playbooks p WHERE p.id=run.playbook_id AND(p.organization_id IS NULL OR p.organization_id=NEW.organization_id))
 OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id JOIN app.organizations o ON o.id=m.organization_id
 WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role IN('ORG_ADMIN','ANALYST') AND o.status='ACTIVE' AND u.is_active AND(u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp())
 AND(m.role='ORG_ADMIN' OR m.id=parent.assigned_membership_id OR m.id=task.assigned_membership_id))
 OR NOT EXISTS(SELECT 1 FROM audit.audit_events a WHERE a.organization_id=NEW.organization_id AND a.resource_id=NEW.id AND a.actor_user_id=NEW.actor_user_id AND a.correlation_id=NEW.correlation_id AND a.action='ai.assistance.evaluated' AND a.outcome='SUCCESS' AND a.details_json->>'outcome'=NEW.outcome)
 THEN RAISE EXCEPTION 'Invalid AI context, scope, actor or atomic audit' USING ERRCODE='23514'; END IF;
 IF NEW.outcome='VALID' THEN
 recommendation:=NEW.recommendation_json::jsonb;
 IF parent.status<>'IN_RESPONSE' OR run.status<>'IN_PROGRESS' OR task.status NOT IN('PENDING','BLOCKED','IN_PROGRESS')
 OR NEW.incident_revision<>parent.revision OR NEW.execution_revision<>run.revision OR NEW.context_token IS DISTINCT FROM app.closure_risk_context(NEW.organization_id,NEW.incident_id)
 OR NOT app.execution_version_usable(run.id) OR NEW.model_digest IS NULL OR NEW.model='unconfigured'
 OR (SELECT count(*) FROM jsonb_object_keys(recommendation))<>4 OR NOT ARRAY(SELECT jsonb_object_keys(recommendation))<@ARRAY['stepCode','guidance','cautions','assumptions']::text[]
 OR recommendation->>'stepCode' IS DISTINCT FROM step.code OR jsonb_typeof(recommendation->'guidance')<>'string' OR length(btrim(recommendation->>'guidance')) NOT BETWEEN 1 AND 4000
 OR jsonb_typeof(recommendation->'cautions')<>'array' OR jsonb_typeof(recommendation->'assumptions')<>'array'
 THEN RAISE EXCEPTION 'AI recommendation requires current valid context' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END;$check$;
CREATE CONSTRAINT TRIGGER check_assistance_assessment AFTER INSERT ON app.assistance_assessments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_assistance_assessment();

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $down$ BEGIN IF EXISTS(SELECT 1 FROM app.assistance_assessments) THEN RAISE EXCEPTION 'Cannot remove recorded AI assessments'; END IF; END;$down$;
DROP TRIGGER check_assistance_assessment ON app.assistance_assessments;
DROP TRIGGER protect_assistance_history ON app.assistance_assessments;
DROP TRIGGER truncate_assistance_history ON app.assistance_assessments;
DROP FUNCTION app.check_assistance_assessment();
DROP FUNCTION app.protect_assistance_history();
""");
            migrationBuilder.DropTable(
                name: "assistance_assessments",
                schema: "app");
        }
    }
}
