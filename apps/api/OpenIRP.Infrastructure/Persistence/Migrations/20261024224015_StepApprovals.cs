using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StepApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.CreateTable(
                name: "step_approvals",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    execution_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    self_approval = table.Column<bool>(type: "boolean", nullable: false),
                    self_approval_justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_approvals", x => x.id);
                    table.UniqueConstraint("ak_step_approval_scope", x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.assigned_membership_id, x.id });
                    table.CheckConstraint("ck_step_approval_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                    table.CheckConstraint("ck_step_approval_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
                    table.CheckConstraint("ck_step_approval_values", "execution_revision>1 AND btrim(reason)<>''");
                    table.ForeignKey(
                        name: "fk_step_approvals_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_step_approvals_execution_tasks_organization_id_incident_id_",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_step_approvals_organization_memberships_organization_id_ass",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_step_approvals_step_approvals_organization_id_incident_id_v",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.assigned_membership_id, x.request_id },
                        principalSchema: "app",
                        principalTable: "step_approvals",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "assigned_membership_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED'))");

            migrationBuilder.CreateIndex(
                name: "ix_step_approvals_actor_user_id",
                schema: "app",
                table: "step_approvals",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_step_approvals_organization_id_assigned_membership_id",
                schema: "app",
                table: "step_approvals",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_step_approvals_organization_id_incident_id_version_id_execu",
                schema: "app",
                table: "step_approvals",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "assigned_membership_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_step_approvals_request_id",
                schema: "app",
                table: "step_approvals",
                column: "request_id",
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_step_approvals_task_id_execution_revision",
                schema: "app",
                table: "step_approvals",
                columns: new[] { "task_id", "execution_revision" },
                unique: true);
            migrationBuilder.Sql("""

                CREATE FUNCTION app.has_step_approval(target uuid, assigned uuid) RETURNS boolean LANGUAGE sql STABLE AS $body$
                 SELECT EXISTS (
                  SELECT 1 FROM app.step_approvals r JOIN app.step_approvals d ON d.request_id=r.id AND d.action='APPROVED'
                  WHERE r.task_id=target AND r.assigned_membership_id=assigned AND r.action='REQUESTED'
                    AND NOT EXISTS (SELECT 1 FROM app.step_approvals newer WHERE newer.task_id=target AND newer.action='REQUESTED' AND newer.execution_revision>r.execution_revision)
                    AND NOT EXISTS (SELECT 1 FROM app.execution_task_events e WHERE e.task_id=target AND e.action='EXECUTION_TASK_ASSIGNED' AND e.revision>r.execution_revision)
                 );
                $body$;
                CREATE FUNCTION app.protect_step_approval() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE task app.execution_tasks%ROWTYPE; req app.step_approvals%ROWTYPE; latest app.step_approvals%ROWTYPE; run_status text; version_status text; incident_status text; assignee uuid;
                BEGIN
                 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Step approvals are append-only' USING ERRCODE='42501'; END IF;
                 SELECT e.status,v.status,i.status INTO run_status,version_status,incident_status
                 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                 JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                 JOIN app.organizations o ON o.id=e.organization_id AND o.status='ACTIVE'
                 WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id FOR UPDATE OF e;
                 IF NOT FOUND OR run_status<>'IN_PROGRESS' OR version_status<>'PUBLISHED' OR incident_status<>'IN_RESPONSE' THEN
                  RAISE EXCEPTION 'Approval requires active usable execution' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND execution_id=NEW.execution_id
                  AND version_id=NEW.version_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id FOR UPDATE;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Approval task scope not found' USING ERRCODE='23503'; END IF;
                 IF task.status NOT IN ('PENDING','BLOCKED') OR task.assigned_membership_id IS DISTINCT FROM NEW.assigned_membership_id
                  OR NOT EXISTS(SELECT 1 FROM app.playbook_steps s WHERE s.id=task.step_id AND s.version_id=NEW.version_id AND s.requires_approval) THEN
                  RAISE EXCEPTION 'Approval requires assigned unstarted controlled task' USING ERRCODE='23514';
                 END IF;
                 SELECT m.user_id INTO assignee FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                  AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP);
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users u WHERE u.id=NEW.actor_user_id AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Approval participants must be active' USING ERRCODE='23514';
                 END IF;
                 IF EXISTS(SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=task.step_id
                  AND NOT EXISTS(SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
                  RAISE EXCEPTION 'Approval requires completed dependencies' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO latest FROM app.step_approvals WHERE task_id=NEW.task_id AND action='REQUESTED' ORDER BY execution_revision DESC LIMIT 1;
                 IF latest.id IS NOT NULL AND (latest.assigned_membership_id<>NEW.assigned_membership_id OR EXISTS
                  (SELECT 1 FROM app.execution_task_events e WHERE e.task_id=NEW.task_id AND e.action='EXECUTION_TASK_ASSIGNED' AND e.revision>latest.execution_revision)) THEN latest.id=NULL; END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF NEW.actor_user_id<>assignee OR (latest.id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM app.step_approvals d WHERE d.request_id=latest.id AND d.action='REJECTED')) THEN
                   RAISE EXCEPTION 'Only assignee may request a fresh approval' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  SELECT * INTO req FROM app.step_approvals WHERE id=NEW.request_id AND task_id=NEW.task_id AND action='REQUESTED';
                  IF NOT FOUND OR latest.id IS DISTINCT FROM req.id OR req.execution_revision>=NEW.execution_revision
                   OR EXISTS(SELECT 1 FROM app.step_approvals d WHERE d.request_id=req.id)
                   OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.role='ORG_ADMIN' AND m.status='ACTIVE') THEN
                   RAISE EXCEPTION 'Decision requires current request and organization administrator' USING ERRCODE='23514';
                  END IF;
                  IF NEW.self_approval IS DISTINCT FROM (NEW.action='APPROVED' AND NEW.actor_user_id=req.actor_user_id) THEN
                   RAISE EXCEPTION 'Self approval requires explicit exception' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_step_approval BEFORE INSERT OR UPDATE OR DELETE ON app.step_approvals FOR EACH ROW EXECUTE FUNCTION app.protect_step_approval();
                CREATE TRIGGER protect_step_approval_truncate BEFORE TRUNCATE ON app.step_approvals FOR EACH STATEMENT EXECUTE FUNCTION app.protect_step_approval();

                CREATE FUNCTION app.check_step_approval_history() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events e WHERE e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                  AND e.version_id=NEW.version_id AND e.execution_id=NEW.execution_id AND e.task_id=NEW.task_id AND e.revision=NEW.execution_revision
                  AND e.actor_user_id=NEW.actor_user_id AND e.correlation_id=NEW.correlation_id AND e.assigned_membership_id=NEW.assigned_membership_id
                  AND e.action='STEP_APPROVAL_'||NEW.action) THEN
                  RAISE EXCEPTION 'Approval must have matching atomic execution history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_step_approval_history AFTER INSERT ON app.step_approvals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_step_approval_history();
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN IF EXISTS(SELECT 1 FROM app.step_approvals) OR EXISTS(SELECT 1 FROM app.incident_events WHERE action LIKE 'STEP_APPROVAL_%') THEN RAISE EXCEPTION 'Cannot roll back step approvals while approval data or history exists'; END IF; END; $guard$;

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
                DROP FUNCTION app.has_step_approval(uuid,uuid);
                """);
            migrationBuilder.DropTable(
                name: "step_approvals",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED'))");
            migrationBuilder.Sql("DROP FUNCTION app.protect_step_approval(); DROP FUNCTION app.check_step_approval_history();");
        }
    }
}
