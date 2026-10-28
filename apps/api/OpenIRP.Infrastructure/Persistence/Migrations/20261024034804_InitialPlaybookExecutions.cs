using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlaybookExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.CreateTable(
                name: "playbook_executions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    playbook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finish_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_executions", x => x.id);
                    table.UniqueConstraint("ak_playbook_executions_organization_id_incident_id_version_id_", x => new { x.organization_id, x.incident_id, x.version_id, x.id });
                    table.CheckConstraint("ck_execution_finish", "(status='IN_PROGRESS' AND finished_at IS NULL AND finish_reason IS NULL) OR (status<>'IN_PROGRESS' AND finished_at IS NOT NULL AND finished_at >= created_at AND finish_reason IS NOT NULL AND btrim(finish_reason)<>'')");
                    table.CheckConstraint("ck_execution_revision", "revision > 0");
                    table.CheckConstraint("ck_execution_status", "status IN ('IN_PROGRESS','COMPLETED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_playbook_executions_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_executions_incidents_organization_id_incident_id",
                        columns: x => new { x.organization_id, x.incident_id },
                        principalSchema: "app",
                        principalTable: "incidents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_executions_playbook_versions_playbook_id_version_id",
                        columns: x => new { x.playbook_id, x.version_id },
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumns: new[] { "playbook_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_tasks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_tasks", x => x.id);
                    table.UniqueConstraint("ak_execution_tasks_organization_id_incident_id_version_id_exec", x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.id });
                    table.CheckConstraint("ck_execution_task_revision", "changed_at_revision > 0");
                    table.CheckConstraint("ck_execution_task_status", "status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED')");
                    table.ForeignKey(
                        name: "fk_execution_tasks_organization_memberships_organization_id_as",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_tasks_playbook_executions_organization_id_inciden",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id },
                        principalSchema: "app",
                        principalTable: "playbook_executions",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_tasks_playbook_steps_version_id_step_id",
                        columns: x => new { x.version_id, x.step_id },
                        principalSchema: "app",
                        principalTable: "playbook_steps",
                        principalColumns: new[] { "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_task_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    execution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    task_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_task_events", x => x.id);
                    table.CheckConstraint("ck_execution_event_action", "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");
                    table.CheckConstraint("ck_execution_event_task", "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED'))");
                    table.CheckConstraint("ck_execution_event_values", "revision > 0 AND incident_revision > 0 AND btrim(reason)<>'' AND execution_status IN ('IN_PROGRESS','COMPLETED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_execution_task_events_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_task_events_execution_tasks_organization_id_incid",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_task_events_organization_memberships_organization",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_task_events_playbook_executions_organization_id_i",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id },
                        principalSchema: "app",
                        principalTable: "playbook_executions",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.CreateIndex(
                name: "ix_execution_task_events_actor_user_id",
                schema: "app",
                table: "execution_task_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_execution_task_events_execution_id_revision",
                schema: "app",
                table: "execution_task_events",
                columns: new[] { "execution_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_task_events_organization_id_assigned_membership_id",
                schema: "app",
                table: "execution_task_events",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_task_events_organization_id_incident_id_version_i",
                schema: "app",
                table: "execution_task_events",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_tasks_execution_id_step_id",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "execution_id", "step_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_tasks_organization_id_assigned_membership_id",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_tasks_version_id_step_id",
                schema: "app",
                table: "execution_tasks",
                columns: new[] { "version_id", "step_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_executions_created_by_user_id",
                schema: "app",
                table: "playbook_executions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_executions_playbook_id_version_id",
                schema: "app",
                table: "playbook_executions",
                columns: new[] { "playbook_id", "version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_incident_active_execution",
                schema: "app",
                table: "playbook_executions",
                columns: new[] { "organization_id", "incident_id" },
                unique: true,
                filter: "status='IN_PROGRESS'");
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_execution_header() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE source_org uuid; current_version uuid; version_status text; incident_status text; incident_category uuid; version_category uuid;
                BEGIN
                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Executions cannot be physically removed' USING ERRCODE='42501'; END IF;
                 IF TG_OP='UPDATE' THEN
                  IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                    OR NEW.incident_id IS DISTINCT FROM OLD.incident_id OR NEW.playbook_id IS DISTINCT FROM OLD.playbook_id
                    OR NEW.version_id IS DISTINCT FROM OLD.version_id OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
                    OR NEW.created_at IS DISTINCT FROM OLD.created_at OR NEW.revision <> OLD.revision+1 OR OLD.status <> 'IN_PROGRESS'
                    OR NEW.status NOT IN ('IN_PROGRESS','COMPLETED','CANCELLED') THEN
                   RAISE EXCEPTION 'Execution identity is immutable and terminal executions cannot change' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                 END IF;
                 SELECT p.organization_id,p.current_published_version_id,v.status,v.category_id INTO source_org,current_version,version_status,version_category
                 FROM app.playbooks p JOIN app.playbook_versions v ON v.playbook_id=p.id WHERE p.id=NEW.playbook_id AND v.id=NEW.version_id FOR SHARE OF p,v;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Execution version not found' USING ERRCODE='23503'; END IF;
                 SELECT status,category_id INTO incident_status,incident_category FROM app.incidents
                 WHERE id=NEW.incident_id AND organization_id=NEW.organization_id FOR SHARE;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Execution incident not found' USING ERRCODE='23503'; END IF;
                 IF (source_org IS NOT NULL AND source_org <> NEW.organization_id) OR current_version IS DISTINCT FROM NEW.version_id
                    OR version_status <> 'PUBLISHED' OR incident_status <> 'IN_RESPONSE' OR incident_category <> version_category
                    OR NEW.status <> 'IN_PROGRESS' OR NEW.revision <> 1 THEN
                  RAISE EXCEPTION 'Execution requires a current published version and incident in response in the same scope' USING ERRCODE='23514';
                 END IF;
                 IF NEW.status='COMPLETED' AND NOT EXISTS(SELECT 1 FROM app.playbook_versions WHERE id=NEW.version_id AND status='PUBLISHED') THEN
                  RAISE EXCEPTION 'Withdrawn execution cannot complete' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_execution_header BEFORE INSERT OR UPDATE OR DELETE ON app.playbook_executions FOR EACH ROW EXECUTE FUNCTION app.protect_execution_header();
                CREATE TRIGGER protect_execution_truncate BEFORE TRUNCATE ON app.playbook_executions FOR EACH STATEMENT EXECUTE FUNCTION app.protect_execution_header();

                CREATE FUNCTION app.protect_execution_task() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                  IF approval OR (NEW.status='COMPLETED' AND evidence) THEN
                   RAISE EXCEPTION 'Approval/evidence controls cannot be bypassed' USING ERRCODE='23514';
                  END IF;
                  IF EXISTS (SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=NEW.step_id
                      AND NOT EXISTS (SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_execution_task BEFORE INSERT OR UPDATE OR DELETE ON app.execution_tasks FOR EACH ROW EXECUTE FUNCTION app.protect_execution_task();
                CREATE TRIGGER protect_execution_task_truncate BEFORE TRUNCATE ON app.execution_tasks FOR EACH STATEMENT EXECUTE FUNCTION app.protect_execution_task();

                CREATE FUNCTION app.check_execution_integrity() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE target uuid; source_version uuid; run_status text; run_revision bigint;
                BEGIN
                 IF TG_TABLE_NAME='execution_tasks' THEN target=NEW.execution_id; ELSE target=NEW.id; END IF;
                 SELECT version_id,status,revision INTO source_version,run_status,run_revision FROM app.playbook_executions WHERE id=target;
                 IF NOT FOUND THEN RETURN NEW; END IF;
                 IF (SELECT count(*) FROM app.execution_tasks WHERE execution_id=target) <>
                    (SELECT count(*) FROM app.playbook_steps WHERE version_id=source_version)
                    OR NOT EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target)
                    OR EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target AND changed_at_revision > run_revision)
                    OR (run_status='COMPLETED' AND EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target AND status <> 'COMPLETED')) THEN
                  RAISE EXCEPTION 'Execution must contain exactly its version tasks and completion must be complete' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_execution_integrity AFTER INSERT OR UPDATE ON app.playbook_executions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_execution_integrity();
                CREATE CONSTRAINT TRIGGER check_execution_task_integrity AFTER INSERT OR UPDATE ON app.execution_tasks DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_execution_integrity();

                CREATE FUNCTION app.protect_execution_events() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Execution events are append-only' USING ERRCODE='42501'; END; $body$;
                CREATE TRIGGER protect_execution_events BEFORE UPDATE OR DELETE ON app.execution_task_events FOR EACH ROW EXECUTE FUNCTION app.protect_execution_events();
                CREATE TRIGGER protect_execution_events_truncate BEFORE TRUNCATE ON app.execution_task_events FOR EACH STATEMENT EXECUTE FUNCTION app.protect_execution_events();

                CREATE FUNCTION app.guard_incident_active_execution() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF (NEW.status <> 'IN_RESPONSE' OR NEW.category_id IS DISTINCT FROM OLD.category_id)
                   AND EXISTS(SELECT 1 FROM app.playbook_executions e WHERE e.organization_id=NEW.organization_id AND e.incident_id=NEW.id AND e.status='IN_PROGRESS') THEN
                  RAISE EXCEPTION 'Active execution prevents leaving response or changing category' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER guard_incident_active_execution BEFORE UPDATE ON app.incidents FOR EACH ROW EXECUTE FUNCTION app.guard_incident_active_execution();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.playbook_executions) OR EXISTS(SELECT 1 FROM app.execution_task_events)
                    OR EXISTS(SELECT 1 FROM app.incident_events WHERE action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) THEN
                  RAISE EXCEPTION 'Cannot roll back executions while execution data or incident history exists';
                 END IF;
                END; $guard$;
                DROP TRIGGER guard_incident_active_execution ON app.incidents;
                DROP FUNCTION app.guard_incident_active_execution();
                """);

            migrationBuilder.DropTable(
                name: "execution_task_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "execution_tasks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbook_executions",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
            migrationBuilder.Sql("DROP FUNCTION app.protect_execution_header(); DROP FUNCTION app.protect_execution_task(); DROP FUNCTION app.check_execution_integrity(); DROP FUNCTION app.protect_execution_events();");
        }
    }
}
