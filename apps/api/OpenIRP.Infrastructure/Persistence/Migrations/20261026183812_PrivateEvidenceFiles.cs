using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivateEvidenceFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence");

            migrationBuilder.CreateTable(
                name: "evidence_files",
                schema: "app",
                columns: table => new
                {
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_files", x => x.evidence_id);
                    table.UniqueConstraint("ak_evidence_files_organization_id_incident_id_evidence_id", x => new { x.organization_id, x.incident_id, x.evidence_id });
                    table.CheckConstraint("ck_file_metadata", "length>0 AND length<=10485760 AND sha256 ~ '^[0-9a-f]{64}$' AND btrim(original_name)<>'' AND content_type IN ('application/pdf','image/png','image/jpeg','text/plain')");
                    table.CheckConstraint("ck_file_state", "revision>0 AND changed_at>=created_at AND ((state IN ('PENDING','AVAILABLE') AND failure_code IS NULL) OR (state IN ('FAILED','QUARANTINED') AND failure_code IN ('missing_file','storage_commit_failed','integrity_check_failed','unsafe_storage_path','withdrawn')))");
                    table.ForeignKey(
                        name: "fk_evidence_files_incident_evidence_organization_id_incident_i",
                        columns: x => new { x.organization_id, x.incident_id, x.evidence_id },
                        principalSchema: "app",
                        principalTable: "incident_evidence",
                        principalColumns: new[] { "organization_id", "incident_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_file_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_file_events", x => x.id);
                    table.CheckConstraint("ck_file_event", "revision>0 AND btrim(reason)<>'' AND state IN ('PENDING','AVAILABLE','FAILED','QUARANTINED')");
                    table.ForeignKey(
                        name: "fk_evidence_file_events_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_file_events_evidence_files_organization_id_inciden",
                        columns: x => new { x.organization_id, x.incident_id, x.evidence_id },
                        principalSchema: "app",
                        principalTable: "evidence_files",
                        principalColumns: new[] { "organization_id", "incident_id", "evidence_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence",
                sql: "kind IN ('TEXT','REFERENCE','FILE') AND classification IN ('INTERNAL','CONFIDENTIAL') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_file_events_actor_user_id",
                schema: "app",
                table: "evidence_file_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_file_events_evidence_id_revision",
                schema: "app",
                table: "evidence_file_events",
                columns: new[] { "evidence_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evidence_file_events_organization_id_incident_id_evidence_id",
                schema: "app",
                table: "evidence_file_events",
                columns: new[] { "organization_id", "incident_id", "evidence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_files_storage_id",
                schema: "app",
                table: "evidence_files",
                column: "storage_id",
                unique: true);
            migrationBuilder.Sql("""

                CREATE FUNCTION app.protect_evidence_file() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Evidence files cannot be removed' USING ERRCODE='42501'; END IF;
                 IF TG_OP='INSERT' THEN
                  IF NEW.state<>'PENDING' OR NEW.revision<>1 OR NOT EXISTS(SELECT 1 FROM app.incident_evidence e WHERE e.id=NEW.evidence_id
                    AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.kind='FILE' AND e.status='ACTIVE') THEN
                   RAISE EXCEPTION 'File metadata starts pending for its exact FILE evidence' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  IF ROW(NEW.evidence_id,NEW.organization_id,NEW.incident_id,NEW.storage_id,NEW.original_name,NEW.content_type,NEW.length,NEW.sha256,NEW.created_at)
                   IS DISTINCT FROM ROW(OLD.evidence_id,OLD.organization_id,OLD.incident_id,OLD.storage_id,OLD.original_name,OLD.content_type,OLD.length,OLD.sha256,OLD.created_at)
                   OR NEW.revision<>OLD.revision+1 OR OLD.state='QUARANTINED'
                   OR NOT ((OLD.state IN ('PENDING','FAILED') AND NEW.state IN ('AVAILABLE','FAILED','QUARANTINED')) OR (OLD.state='AVAILABLE' AND NEW.state='QUARANTINED')) THEN
                   RAISE EXCEPTION 'File metadata is immutable and state transitions are controlled' USING ERRCODE='23514';
                  END IF;
                  IF NEW.state='AVAILABLE' AND NOT EXISTS(SELECT 1 FROM app.incident_evidence e WHERE e.id=NEW.evidence_id AND e.status='ACTIVE') THEN
                   RAISE EXCEPTION 'Withdrawn files cannot become available' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_evidence_file BEFORE INSERT OR UPDATE OR DELETE ON app.evidence_files FOR EACH ROW EXECUTE FUNCTION app.protect_evidence_file();
                CREATE TRIGGER protect_evidence_file_truncate BEFORE TRUNCATE ON app.evidence_files FOR EACH STATEMENT EXECUTE FUNCTION app.protect_evidence_file();

                CREATE FUNCTION app.protect_evidence_file_events() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'File events are append-only' USING ERRCODE='42501'; END; $body$;
                CREATE TRIGGER protect_evidence_file_events BEFORE UPDATE OR DELETE ON app.evidence_file_events FOR EACH ROW EXECUTE FUNCTION app.protect_evidence_file_events();
                CREATE TRIGGER protect_evidence_file_events_truncate BEFORE TRUNCATE ON app.evidence_file_events FOR EACH STATEMENT EXECUTE FUNCTION app.protect_evidence_file_events();

                CREATE FUNCTION app.check_evidence_file_integrity() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE target uuid;
                BEGIN
                 IF TG_TABLE_NAME='incident_evidence' THEN
                  IF NEW.kind='FILE' AND NOT EXISTS(SELECT 1 FROM app.evidence_files f WHERE f.evidence_id=NEW.id AND f.organization_id=NEW.organization_id
                     AND f.incident_id=NEW.incident_id AND (NEW.status='ACTIVE' OR f.state='QUARANTINED')) THEN
                   RAISE EXCEPTION 'FILE evidence requires exactly one file and withdrawal quarantine' USING ERRCODE='23514';
                  END IF;
                 ELSIF TG_TABLE_NAME='evidence_files' THEN
                  IF NOT EXISTS(SELECT 1 FROM app.evidence_file_events f WHERE f.evidence_id=NEW.evidence_id AND f.organization_id=NEW.organization_id
                    AND f.incident_id=NEW.incident_id AND f.revision=NEW.revision AND f.state=NEW.state AND f.failure_code IS NOT DISTINCT FROM NEW.failure_code) THEN
                   RAISE EXCEPTION 'File changes require atomic history' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  IF NOT EXISTS(SELECT 1 FROM app.evidence_files f WHERE f.evidence_id=NEW.evidence_id AND f.organization_id=NEW.organization_id
                    AND f.incident_id=NEW.incident_id AND f.revision>=NEW.revision) OR
                    NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN app.organizations o ON o.id=m.organization_id JOIN auth.asp_net_users u ON u.id=m.user_id
                     WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND o.status='ACTIVE'
                      AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)
                      AND (m.role IN ('ORG_ADMIN','ANALYST') OR NEW.state='QUARANTINED' AND NEW.failure_code IN ('missing_file','integrity_check_failed','unsafe_storage_path'))) THEN
                   RAISE EXCEPTION 'File history requires its exact scope and eligible actor' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_evidence_file_kind AFTER INSERT OR UPDATE ON app.incident_evidence DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_evidence_file_integrity();
                CREATE CONSTRAINT TRIGGER check_evidence_file_history AFTER INSERT OR UPDATE ON app.evidence_files DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_evidence_file_integrity();
                CREATE CONSTRAINT TRIGGER check_evidence_file_event_scope AFTER INSERT ON app.evidence_file_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_evidence_file_integrity();

                CREATE OR REPLACE FUNCTION app.protect_execution_task() RETURNS trigger LANGUAGE plpgsql AS $body$
                                                DECLARE run_status text; version_status text; incident_status text; approval boolean; evidence boolean;
                                                BEGIN
                                                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Execution tasks cannot be physically removed' USING ERRCODE='42501'; END IF;
                                                 IF TG_OP='UPDATE' THEN
                                                  IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                                                    OR NEW.incident_id IS DISTINCT FROM OLD.incident_id OR NEW.execution_id IS DISTINCT FROM OLD.execution_id
                                                    OR NEW.version_id IS DISTINCT FROM OLD.version_id OR NEW.step_id IS DISTINCT FROM OLD.step_id
                                                    OR NEW.changed_at_revision <= OLD.changed_at_revision OR OLD.status='COMPLETED' THEN
                                                   RAISE EXCEPTION 'Execution task identity and completed state are immutable' USING ERRCODE='23514';
                                                  END IF;
                                                  IF NEW.status <> OLD.status AND NOT ((OLD.status='PENDING' AND NEW.status IN ('IN_PROGRESS','BLOCKED'))
                                                     OR (OLD.status='BLOCKED' AND NEW.status IN ('PENDING','IN_PROGRESS'))
                                                     OR (OLD.status='IN_PROGRESS' AND NEW.status IN ('BLOCKED','COMPLETED'))) THEN
                                                   RAISE EXCEPTION 'Illegal execution task transition' USING ERRCODE='23514';
                                                  END IF;
                                                 ELSE
                                                  IF NEW.status <> 'PENDING' OR NEW.changed_at_revision <> 1 OR NEW.assigned_membership_id IS NOT NULL THEN
                                                   RAISE EXCEPTION 'Tasks start pending and unassigned' USING ERRCODE='23514';
                                                  END IF;
                                                 END IF;
                                                 SELECT e.status,v.status,i.status INTO run_status,version_status,incident_status
                                                 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                                                 JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                                                 WHERE e.id=NEW.execution_id AND e.version_id=NEW.version_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                                                 FOR UPDATE OF e;
                                                 IF NOT FOUND THEN RAISE EXCEPTION 'Execution scope not found' USING ERRCODE='23503'; END IF;
                                                 IF run_status <> 'IN_PROGRESS' OR version_status <> 'PUBLISHED' OR incident_status <> 'IN_RESPONSE' THEN
                                                  RAISE EXCEPTION 'Execution tasks require an active usable execution' USING ERRCODE='23514';
                                                 END IF;
                                                 SELECT requires_approval,requires_evidence INTO approval,evidence FROM app.playbook_steps WHERE id=NEW.step_id AND version_id=NEW.version_id;
                                                 IF NOT FOUND THEN RAISE EXCEPTION 'Task step belongs to another version' USING ERRCODE='23503'; END IF;
                                                 IF NEW.status IN ('IN_PROGRESS','COMPLETED') THEN
                                                  IF NEW.assigned_membership_id IS NULL OR NOT EXISTS
                                                     (SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                                                      WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE'
                                                       AND m.role IN ('ORG_ADMIN','ANALYST') AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end <= CURRENT_TIMESTAMP)) THEN
                                                   RAISE EXCEPTION 'Task requires an active eligible assignee' USING ERRCODE='23514';
                                                  END IF;
                                                  IF (approval AND (NOT app.has_step_approval(NEW.id,NEW.assigned_membership_id) OR (TG_OP='UPDATE' AND NEW.assigned_membership_id IS DISTINCT FROM OLD.assigned_membership_id))) OR (NEW.status='COMPLETED' AND evidence AND NEW.completion_evidence_id IS NULL) THEN
                                                   RAISE EXCEPTION 'Approval/evidence controls cannot be bypassed' USING ERRCODE='23514';
                                                  END IF;
                                                  IF EXISTS (SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=NEW.step_id
                                                      AND NOT EXISTS (SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
                                                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                                                  END IF;
                                                 END IF;
                                 IF NEW.completion_evidence_id IS NOT NULL THEN
                                  IF NEW.status<>'COMPLETED' THEN RAISE EXCEPTION 'Evidence can only be selected on completion' USING ERRCODE='23514'; END IF;
                                  PERFORM id FROM app.incident_evidence WHERE id=NEW.completion_evidence_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id
                                   AND version_id=NEW.version_id AND execution_id=NEW.execution_id AND task_id=NEW.id AND status='ACTIVE' AND (kind<>'FILE' OR EXISTS(SELECT 1 FROM app.evidence_files f WHERE f.evidence_id=app.incident_evidence.id AND f.state='AVAILABLE')) FOR SHARE;
                                  IF NOT FOUND THEN RAISE EXCEPTION 'Completion requires active evidence of this exact task' USING ERRCODE='23514'; END IF;
                                 END IF;
                                 RETURN NEW;
                                                END; $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN IF EXISTS(SELECT 1 FROM app.evidence_files) OR EXISTS(SELECT 1 FROM app.evidence_file_events) OR EXISTS(SELECT 1 FROM app.incident_evidence WHERE kind='FILE') THEN RAISE EXCEPTION 'Cannot roll back private files while file evidence or history exists'; END IF; END; $guard$;
                CREATE OR REPLACE FUNCTION app.protect_execution_task() RETURNS trigger LANGUAGE plpgsql AS $body$
                                                DECLARE run_status text; version_status text; incident_status text; approval boolean; evidence boolean;
                                                BEGIN
                                                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Execution tasks cannot be physically removed' USING ERRCODE='42501'; END IF;
                                                 IF TG_OP='UPDATE' THEN
                                                  IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                                                    OR NEW.incident_id IS DISTINCT FROM OLD.incident_id OR NEW.execution_id IS DISTINCT FROM OLD.execution_id
                                                    OR NEW.version_id IS DISTINCT FROM OLD.version_id OR NEW.step_id IS DISTINCT FROM OLD.step_id
                                                    OR NEW.changed_at_revision <= OLD.changed_at_revision OR OLD.status='COMPLETED' THEN
                                                   RAISE EXCEPTION 'Execution task identity and completed state are immutable' USING ERRCODE='23514';
                                                  END IF;
                                                  IF NEW.status <> OLD.status AND NOT ((OLD.status='PENDING' AND NEW.status IN ('IN_PROGRESS','BLOCKED'))
                                                     OR (OLD.status='BLOCKED' AND NEW.status IN ('PENDING','IN_PROGRESS'))
                                                     OR (OLD.status='IN_PROGRESS' AND NEW.status IN ('BLOCKED','COMPLETED'))) THEN
                                                   RAISE EXCEPTION 'Illegal execution task transition' USING ERRCODE='23514';
                                                  END IF;
                                                 ELSE
                                                  IF NEW.status <> 'PENDING' OR NEW.changed_at_revision <> 1 OR NEW.assigned_membership_id IS NOT NULL THEN
                                                   RAISE EXCEPTION 'Tasks start pending and unassigned' USING ERRCODE='23514';
                                                  END IF;
                                                 END IF;
                                                 SELECT e.status,v.status,i.status INTO run_status,version_status,incident_status
                                                 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                                                 JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                                                 WHERE e.id=NEW.execution_id AND e.version_id=NEW.version_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                                                 FOR UPDATE OF e;
                                                 IF NOT FOUND THEN RAISE EXCEPTION 'Execution scope not found' USING ERRCODE='23503'; END IF;
                                                 IF run_status <> 'IN_PROGRESS' OR version_status <> 'PUBLISHED' OR incident_status <> 'IN_RESPONSE' THEN
                                                  RAISE EXCEPTION 'Execution tasks require an active usable execution' USING ERRCODE='23514';
                                                 END IF;
                                                 SELECT requires_approval,requires_evidence INTO approval,evidence FROM app.playbook_steps WHERE id=NEW.step_id AND version_id=NEW.version_id;
                                                 IF NOT FOUND THEN RAISE EXCEPTION 'Task step belongs to another version' USING ERRCODE='23503'; END IF;
                                                 IF NEW.status IN ('IN_PROGRESS','COMPLETED') THEN
                                                  IF NEW.assigned_membership_id IS NULL OR NOT EXISTS
                                                     (SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                                                      WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE'
                                                       AND m.role IN ('ORG_ADMIN','ANALYST') AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end <= CURRENT_TIMESTAMP)) THEN
                                                   RAISE EXCEPTION 'Task requires an active eligible assignee' USING ERRCODE='23514';
                                                  END IF;
                                                  IF (approval AND (NOT app.has_step_approval(NEW.id,NEW.assigned_membership_id) OR (TG_OP='UPDATE' AND NEW.assigned_membership_id IS DISTINCT FROM OLD.assigned_membership_id))) OR (NEW.status='COMPLETED' AND evidence AND NEW.completion_evidence_id IS NULL) THEN
                                                   RAISE EXCEPTION 'Approval/evidence controls cannot be bypassed' USING ERRCODE='23514';
                                                  END IF;
                                                  IF EXISTS (SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=NEW.step_id
                                                      AND NOT EXISTS (SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
                                                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                                                  END IF;
                                                 END IF;
                                 IF NEW.completion_evidence_id IS NOT NULL THEN
                                  IF NEW.status<>'COMPLETED' THEN RAISE EXCEPTION 'Evidence can only be selected on completion' USING ERRCODE='23514'; END IF;
                                  PERFORM id FROM app.incident_evidence WHERE id=NEW.completion_evidence_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id
                                   AND version_id=NEW.version_id AND execution_id=NEW.execution_id AND task_id=NEW.id AND status='ACTIVE' FOR SHARE;
                                  IF NOT FOUND THEN RAISE EXCEPTION 'Completion requires active evidence of this exact task' USING ERRCODE='23514'; END IF;
                                 END IF;
                                 RETURN NEW;
                                                END; $body$;
                DROP TRIGGER check_evidence_file_kind ON app.incident_evidence;
                """);

            migrationBuilder.DropTable(
                name: "evidence_file_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "evidence_files",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence",
                sql: "kind IN ('TEXT','REFERENCE') AND classification IN ('INTERNAL','CONFIDENTIAL') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");
            migrationBuilder.Sql("DROP FUNCTION app.protect_evidence_file(); DROP FUNCTION app.protect_evidence_file_events(); DROP FUNCTION app.check_evidence_file_integrity();");
        }
    }
}
