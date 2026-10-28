using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentTextReferenceEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<Guid>(
                name: "completion_evidence_id",
                schema: "app",
                table: "execution_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incident_evidence",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_evidence", x => x.id);
                    table.UniqueConstraint("ak_incident_evidence_organization_id_incident_id_id", x => new { x.organization_id, x.incident_id, x.id });
                    table.CheckConstraint("ck_evidence_content", "kind IN ('TEXT','REFERENCE') AND classification IN ('INTERNAL','CONFIDENTIAL') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");
                    table.CheckConstraint("ck_evidence_lifecycle", "(status='ACTIVE' AND revision=1 AND withdrawn_by_user_id IS NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL) OR (status='WITHDRAWN' AND revision=2 AND withdrawn_by_user_id IS NOT NULL AND withdrawn_at IS NOT NULL AND withdrawn_at>=created_at AND withdrawal_reason IS NOT NULL AND btrim(withdrawal_reason)<>'')");
                    table.CheckConstraint("ck_evidence_task_scope", "(version_id IS NULL AND execution_id IS NULL AND task_id IS NULL) OR (version_id IS NOT NULL AND execution_id IS NOT NULL AND task_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_incident_evidence_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_evidence_asp_net_users_withdrawn_by_user_id",
                        column: x => x.withdrawn_by_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_evidence_execution_tasks_organization_id_incident_",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_evidence_incidents_organization_id_incident_id",
                        columns: x => new { x.organization_id, x.incident_id },
                        principalSchema: "app",
                        principalTable: "incidents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_events", x => x.id);
                    table.CheckConstraint("ck_evidence_event", "(action='REGISTERED' AND revision=1 OR action='WITHDRAWN' AND revision=2) AND btrim(reason)<>''");
                    table.ForeignKey(
                        name: "fk_evidence_events_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_events_incident_evidence_organization_id_incident_",
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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.CreateIndex(
                name: "ix_execution_tasks_organization_id_incident_id_completion_evid",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "organization_id", "incident_id", "completion_evidence_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_task_completion_evidence",
                schema: "app",
                table: "execution_tasks",
                sql: "completion_evidence_id IS NULL OR status='COMPLETED'");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_events_actor_user_id",
                schema: "app",
                table: "evidence_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_events_evidence_id_revision",
                schema: "app",
                table: "evidence_events",
                columns: new[] { "evidence_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evidence_events_organization_id_incident_id_evidence_id",
                schema: "app",
                table: "evidence_events",
                columns: new[] { "organization_id", "incident_id", "evidence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_created_by_user_id",
                schema: "app",
                table: "incident_evidence",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_organization_id_incident_id_id",
                schema: "app",
                table: "incident_evidence",
                columns: new[] { "organization_id", "incident_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_organization_id_incident_id_version_id_ex",
                schema: "app",
                table: "incident_evidence",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_task_id_status",
                schema: "app",
                table: "incident_evidence",
                columns: new[] { "task_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_evidence_withdrawn_by_user_id",
                schema: "app",
                table: "incident_evidence",
                column: "withdrawn_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_execution_tasks_incident_evidence_organization_id_incident_",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "organization_id", "incident_id", "completion_evidence_id" },
                principalSchema: "app",
                principalTable: "incident_evidence",
                principalColumns: new[] { "organization_id", "incident_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""

                CREATE FUNCTION app.protect_incident_evidence() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE actor uuid;
                BEGIN
                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Evidence cannot be physically removed' USING ERRCODE='42501'; END IF;
                 IF TG_OP='UPDATE' THEN
                  IF ROW(NEW.id,NEW.organization_id,NEW.incident_id,NEW.version_id,NEW.execution_id,NEW.task_id,NEW.kind,NEW.classification,NEW.title,NEW.content,NEW.created_by_user_id,NEW.created_at)
                    IS DISTINCT FROM ROW(OLD.id,OLD.organization_id,OLD.incident_id,OLD.version_id,OLD.execution_id,OLD.task_id,OLD.kind,OLD.classification,OLD.title,OLD.content,OLD.created_by_user_id,OLD.created_at)
                    OR OLD.status<>'ACTIVE' OR NEW.status<>'WITHDRAWN' OR NEW.revision<>OLD.revision+1 THEN
                   RAISE EXCEPTION 'Evidence content/identity are immutable and withdrawal is terminal' USING ERRCODE='23514';
                  END IF;
                  actor=NEW.withdrawn_by_user_id;
                 ELSE
                  IF NEW.status<>'ACTIVE' OR NEW.revision<>1 OR NOT EXISTS(SELECT 1 FROM app.incidents WHERE id=NEW.incident_id AND organization_id=NEW.organization_id AND status NOT IN ('CLOSED','DISCARDED')) THEN
                   RAISE EXCEPTION 'Evidence requires an editable incident' USING ERRCODE='23514';
                  END IF;
                  IF NEW.task_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM app.execution_tasks t JOIN app.playbook_executions e ON e.id=t.execution_id
                   JOIN app.incidents i ON i.id=t.incident_id AND i.organization_id=t.organization_id
                   WHERE t.id=NEW.task_id AND t.execution_id=NEW.execution_id AND t.version_id=NEW.version_id AND t.organization_id=NEW.organization_id
                    AND t.incident_id=NEW.incident_id AND t.status<>'COMPLETED' AND e.status='IN_PROGRESS' AND i.status='IN_RESPONSE') THEN
                   RAISE EXCEPTION 'Evidence task must belong to the active incident execution' USING ERRCODE='23503';
                  END IF;
                  actor=NEW.created_by_user_id;
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN app.organizations o ON o.id=m.organization_id JOIN auth.asp_net_users u ON u.id=m.user_id
                  WHERE m.organization_id=NEW.organization_id AND m.user_id=actor AND m.status='ACTIVE' AND o.status='ACTIVE'
                   AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)
                   AND (m.role='ORG_ADMIN' OR TG_OP='INSERT' AND m.role='ANALYST')) THEN
                  RAISE EXCEPTION 'Evidence operation requires an eligible organization actor' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_incident_evidence BEFORE INSERT OR UPDATE OR DELETE ON app.incident_evidence FOR EACH ROW EXECUTE FUNCTION app.protect_incident_evidence();
                CREATE TRIGGER protect_incident_evidence_truncate BEFORE TRUNCATE ON app.incident_evidence FOR EACH STATEMENT EXECUTE FUNCTION app.protect_incident_evidence();

                CREATE FUNCTION app.protect_evidence_events() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Evidence events are append-only' USING ERRCODE='42501'; END; $body$;
                CREATE TRIGGER protect_evidence_events BEFORE UPDATE OR DELETE ON app.evidence_events FOR EACH ROW EXECUTE FUNCTION app.protect_evidence_events();
                CREATE TRIGGER protect_evidence_events_truncate BEFORE TRUNCATE ON app.evidence_events FOR EACH STATEMENT EXECUTE FUNCTION app.protect_evidence_events();

                CREATE FUNCTION app.check_evidence_history() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE target uuid; rev bigint; who uuid; org uuid; inc uuid; operation text;
                BEGIN
                 IF TG_TABLE_NAME='incident_evidence' THEN
                  target=NEW.id; rev=NEW.revision; org=NEW.organization_id; inc=NEW.incident_id;
                  IF rev=1 THEN who=NEW.created_by_user_id; operation='REGISTERED'; ELSE who=NEW.withdrawn_by_user_id; operation='WITHDRAWN'; END IF;
                  IF NOT EXISTS(SELECT 1 FROM app.evidence_events e WHERE e.evidence_id=target AND e.revision=rev AND e.organization_id=org
                   AND e.incident_id=inc AND e.actor_user_id=who AND e.action=operation) THEN
                   RAISE EXCEPTION 'Evidence changes require atomic history' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  IF NOT EXISTS(SELECT 1 FROM app.incident_evidence e WHERE e.id=NEW.evidence_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                   AND ((NEW.revision=1 AND NEW.actor_user_id=e.created_by_user_id) OR (NEW.revision=2 AND e.revision=2 AND NEW.actor_user_id=e.withdrawn_by_user_id))) THEN
                   RAISE EXCEPTION 'Evidence history must match its evidence lifecycle and actor' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_evidence_history AFTER INSERT OR UPDATE ON app.incident_evidence DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_evidence_history();
                CREATE CONSTRAINT TRIGGER check_evidence_event_history AFTER INSERT ON app.evidence_events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_evidence_history();

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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN IF EXISTS(SELECT 1 FROM app.incident_evidence) OR EXISTS(SELECT 1 FROM app.evidence_events) OR EXISTS(SELECT 1 FROM app.execution_tasks WHERE completion_evidence_id IS NOT NULL) OR EXISTS(SELECT 1 FROM app.incident_events WHERE action='EVIDENCE_REGISTERED') THEN RAISE EXCEPTION 'Cannot roll back evidence while evidence or history exists'; END IF; END; $guard$;
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
                                  IF (approval AND (NOT app.has_step_approval(NEW.id,NEW.assigned_membership_id) OR (TG_OP='UPDATE' AND NEW.assigned_membership_id IS DISTINCT FROM OLD.assigned_membership_id))) OR (NEW.status='COMPLETED' AND evidence) THEN
                                   RAISE EXCEPTION 'Approval/evidence controls cannot be bypassed' USING ERRCODE='23514';
                                  END IF;
                                  IF EXISTS (SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=NEW.step_id
                                      AND NOT EXISTS (SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
                                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                                  END IF;
                                 END IF;
                                 RETURN NEW;
                                END; $body$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_execution_tasks_incident_evidence_organization_id_incident_",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.DropTable(
                name: "evidence_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "incident_evidence",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropIndex(
                name: "ix_execution_tasks_organization_id_incident_id_completion_evid",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_task_completion_evidence",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.DropColumn(
                name: "completion_evidence_id",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
            migrationBuilder.Sql("DROP FUNCTION app.protect_incident_evidence(); DROP FUNCTION app.protect_evidence_events(); DROP FUNCTION app.check_evidence_history();");
        }
    }
}
