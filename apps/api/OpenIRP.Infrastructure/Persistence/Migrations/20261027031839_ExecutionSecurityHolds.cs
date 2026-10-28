using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionSecurityHolds : Migration
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
                name: "playbook_security_holds",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playbook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_security_holds", x => x.id);
                    table.UniqueConstraint("ak_playbook_security_holds_version_id_id", x => new { x.version_id, x.id });
                    table.CheckConstraint("ck_security_hold_values", "revision>1 AND btrim(reason)<>''");
                    table.ForeignKey(
                        name: "fk_playbook_security_holds_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_security_holds_playbook_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_security_holds_playbooks_playbook_id",
                        column: x => x.playbook_id,
                        principalSchema: "app",
                        principalTable: "playbooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_security_decisions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    execution_revision = table.Column<long>(type: "bigint", nullable: false),
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_security_decisions", x => x.id);
                    table.CheckConstraint("ck_security_decision_values", "action IN ('CONTINUE','CANCEL') AND execution_revision>1 AND incident_revision>1 AND btrim(reason)<>''");
                    table.ForeignKey(
                        name: "fk_execution_security_decisions_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_security_decisions_playbook_executions_organizati",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id },
                        principalSchema: "app",
                        principalTable: "playbook_executions",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_execution_security_decisions_playbook_security_holds_versio",
                        columns: x => new { x.version_id, x.security_hold_id },
                        principalSchema: "app",
                        principalTable: "playbook_security_holds",
                        principalColumns: new[] { "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED','SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED'))");

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_actor_user_id",
                schema: "app",
                table: "execution_security_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_execution_id_execution_revision",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "execution_id", "execution_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_execution_id_security_hold_id_",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "execution_id", "security_hold_id", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_organization_id_incident_id_ve",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_security_decisions_version_id_security_hold_id",
                schema: "app",
                table: "execution_security_decisions",
                columns: new[] { "version_id", "security_hold_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_security_holds_actor_user_id",
                schema: "app",
                table: "playbook_security_holds",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_security_holds_playbook_id",
                schema: "app",
                table: "playbook_security_holds",
                column: "playbook_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_security_holds_version_id_revision",
                schema: "app",
                table: "playbook_security_holds",
                columns: new[] { "version_id", "revision" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION app.execution_version_usable(run uuid) RETURNS boolean LANGUAGE sql STABLE AS $b$
                 SELECT EXISTS(SELECT 1 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                 WHERE e.id=run AND (v.status='PUBLISHED' OR (v.status='ARCHIVED' AND EXISTS(
                 SELECT 1 FROM app.playbook_security_holds h JOIN app.execution_security_decisions d ON d.security_hold_id=h.id
                  AND d.execution_id=e.id AND d.organization_id=e.organization_id AND d.incident_id=e.incident_id AND d.action='CONTINUE'
                 WHERE h.version_id=e.version_id AND h.id=(SELECT id FROM app.playbook_security_holds WHERE version_id=e.version_id ORDER BY revision DESC LIMIT 1)
                 AND NOT EXISTS(SELECT 1 FROM app.execution_security_decisions c WHERE c.execution_id=e.id AND c.security_hold_id=h.id AND c.action='CANCEL')))));
                $b$;
                CREATE FUNCTION app.protect_security_history() RETURNS trigger LANGUAGE plpgsql AS $b$
                BEGIN
                 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Security history is append-only' USING ERRCODE='42501'; END IF;
                 RETURN NEW;
                END;$b$;
                CREATE OR REPLACE FUNCTION app.protect_step_approval()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                                DECLARE task app.execution_tasks%ROWTYPE; req app.step_approvals%ROWTYPE; latest app.step_approvals%ROWTYPE; run_status text; version_status text; incident_status text; assignee uuid;
                                BEGIN
                                 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Step approvals are append-only' USING ERRCODE='42501'; END IF;
                                 SELECT e.status,v.status,i.status INTO run_status,version_status,incident_status
                                 FROM app.playbook_executions e JOIN app.playbook_versions v ON v.id=e.version_id
                                 JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                                 JOIN app.organizations o ON o.id=e.organization_id AND o.status='ACTIVE'
                                 WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id FOR UPDATE OF e;
                                 IF NOT FOUND OR run_status<>'IN_PROGRESS' OR NOT app.execution_version_usable(NEW.execution_id) OR incident_status<>'IN_RESPONSE' THEN
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
                                 IF NOT app.execution_dependencies_satisfied(NEW.execution_id,NEW.task_id,NEW.assigned_membership_id) THEN
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
                                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.protect_execution_task()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                                                                                DECLARE run_status text; version_status text; incident_status text; approval boolean; evidence boolean;
                                                                                BEGIN
                                                                                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Execution tasks cannot be physically removed' USING ERRCODE='42501'; END IF;
                                                                                 IF TG_OP='UPDATE' THEN
                                                                                  IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                                                                                    OR NEW.incident_id IS DISTINCT FROM OLD.incident_id OR NEW.execution_id IS DISTINCT FROM OLD.execution_id
                                                                                    OR NEW.version_id IS DISTINCT FROM OLD.version_id OR NEW.step_id IS DISTINCT FROM OLD.step_id
                                                                                    OR NEW.changed_at_revision <= OLD.changed_at_revision OR OLD.status IN ('COMPLETED','SKIPPED') THEN
                                                                                   RAISE EXCEPTION 'Execution task identity and completed state are immutable' USING ERRCODE='23514';
                                                                                  END IF;
                                                                                  IF NEW.status <> OLD.status AND NOT ((OLD.status='PENDING' AND NEW.status IN ('IN_PROGRESS','BLOCKED','SKIPPED'))
                                                                                     OR (OLD.status='BLOCKED' AND NEW.status IN ('PENDING','IN_PROGRESS','SKIPPED'))
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
                                                                                 IF run_status <> 'IN_PROGRESS' OR NOT app.execution_version_usable(NEW.execution_id) OR incident_status <> 'IN_RESPONSE' THEN
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
                                                                                  IF NOT app.execution_dependencies_satisfied(NEW.execution_id,NEW.id,NEW.assigned_membership_id) THEN
                                                                                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                                                                                  END IF;
                                                                                 END IF;
                                                                 IF TG_OP='UPDATE' AND NEW.status='SKIPPED' AND NEW.assigned_membership_id IS DISTINCT FROM OLD.assigned_membership_id THEN
                                                  RAISE EXCEPTION 'Skip cannot change assignee' USING ERRCODE='23514';
                                                 END IF;
                                                 IF NEW.completion_evidence_id IS NOT NULL THEN
                                                                  IF NEW.status<>'COMPLETED' THEN RAISE EXCEPTION 'Evidence can only be selected on completion' USING ERRCODE='23514'; END IF;
                                                                  PERFORM id FROM app.incident_evidence WHERE id=NEW.completion_evidence_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id
                                                                   AND version_id=NEW.version_id AND execution_id=NEW.execution_id AND task_id=NEW.id AND status='ACTIVE' AND (kind<>'FILE' OR EXISTS(SELECT 1 FROM app.evidence_files f WHERE f.evidence_id=app.incident_evidence.id AND f.state='AVAILABLE')) FOR SHARE;
                                                                  IF NOT FOUND THEN RAISE EXCEPTION 'Completion requires active evidence of this exact task' USING ERRCODE='23514'; END IF;
                                                                 END IF;
                                                                 RETURN NEW;
                                                                                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.check_skip_authorization()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; req app.skip_authorizations%ROWTYPE; assignee uuid;
                BEGIN
                 PERFORM e.id FROM app.playbook_executions e JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                  JOIN app.playbook_versions v ON v.id=e.version_id JOIN app.organizations o ON o.id=e.organization_id
                  WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id
                   AND e.status='IN_PROGRESS' AND i.status='IN_RESPONSE' AND app.execution_version_usable(NEW.execution_id) AND o.status='ACTIVE'
                   AND e.revision=NEW.execution_revision AND i.revision=NEW.incident_revision FOR UPDATE OF e;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Skip requires current active execution and incident' USING ERRCODE='23514'; END IF;
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id FOR UPDATE;
                 IF NOT FOUND OR task.assigned_membership_id IS DISTINCT FROM NEW.assigned_membership_id
                  OR (NEW.action='APPROVED' AND (task.status<>'SKIPPED' OR task.changed_at_revision<>NEW.execution_revision))
                  OR (NEW.action<>'APPROVED' AND (task.status NOT IN ('PENDING','BLOCKED') OR task.changed_at_revision>=NEW.execution_revision)) THEN
                  RAISE EXCEPTION 'Skip requires matching assigned task and state' USING ERRCODE='23514';
                 END IF;
                 SELECT m.user_id INTO assignee FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE'
                  AND m.role IN ('ORG_ADMIN','ANALYST') AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP);
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users u WHERE u.id=NEW.actor_user_id AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Skip participants must be eligible' USING ERRCODE='23514';
                 END IF;
                 IF NEW.dependency_context_json IS DISTINCT FROM app.skip_dependency_context(NEW.execution_id, NEW.task_id) THEN
                  RAISE EXCEPTION 'Skip dependency context mismatch' USING ERRCODE='23514';
                 END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF NEW.actor_user_id<>assignee OR EXISTS(SELECT 1 FROM app.skip_authorizations r WHERE r.task_id=NEW.task_id AND r.action='REQUESTED'
                    AND r.execution_revision=NEW.execution_revision-1 AND r.incident_revision=NEW.incident_revision-1
                    AND NOT EXISTS(SELECT 1 FROM app.skip_authorizations d WHERE d.request_id=r.id AND d.action='REJECTED')) THEN
                   RAISE EXCEPTION 'Only assignee may request fresh skip authorization' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  SELECT * INTO req FROM app.skip_authorizations WHERE id=NEW.request_id AND action='REQUESTED' AND task_id=NEW.task_id;
                  IF NOT FOUND OR req.execution_revision<>NEW.execution_revision-1 OR req.incident_revision<>NEW.incident_revision-1
                   OR NEW.impact<>req.impact OR NEW.residual_risk<>req.residual_risk OR NEW.dependency_context_json<>req.dependency_context_json
                   OR EXISTS(SELECT 1 FROM app.skip_authorizations d WHERE d.request_id=req.id AND d.id<>NEW.id)
                   OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN')
                   OR NEW.self_approval IS DISTINCT FROM (NEW.action='APPROVED' AND NEW.actor_user_id=req.actor_user_id) THEN
                   RAISE EXCEPTION 'Skip decision requires current request and explicit admin decision' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events e WHERE e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                  AND e.version_id=NEW.version_id AND e.execution_id=NEW.execution_id AND e.task_id=NEW.task_id AND e.revision=NEW.execution_revision
                  AND e.incident_revision=NEW.incident_revision AND e.actor_user_id=NEW.actor_user_id AND e.correlation_id=NEW.correlation_id
                  AND e.assigned_membership_id=NEW.assigned_membership_id AND e.action='SKIP_AUTHORIZATION_'||NEW.action AND e.task_status=task.status)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events i WHERE i.organization_id=NEW.organization_id AND i.incident_id=NEW.incident_id
                    AND i.revision=NEW.incident_revision AND i.actor_user_id=NEW.actor_user_id AND i.correlation_id=NEW.correlation_id
                    AND i.action='SKIP_AUTHORIZATION_'||NEW.action) THEN
                  RAISE EXCEPTION 'Skip requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.check_dependency_exception()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; pred app.execution_tasks%ROWTYPE; req app.dependency_exceptions%ROWTYPE; assignee uuid;
                BEGIN
                 PERFORM e.id FROM app.playbook_executions e JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                  JOIN app.playbook_versions v ON v.id=e.version_id JOIN app.organizations o ON o.id=e.organization_id
                 WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id
                  AND e.status='IN_PROGRESS' AND i.status='IN_RESPONSE' AND app.execution_version_usable(NEW.execution_id) AND o.status='ACTIVE'
                  AND e.revision=NEW.execution_revision AND i.revision=NEW.incident_revision FOR UPDATE OF e;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Exception requires current usable execution and incident' USING ERRCODE='23514'; END IF;
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id FOR UPDATE;
                 IF NOT FOUND OR task.status NOT IN ('PENDING','BLOCKED') OR task.changed_at_revision>=NEW.execution_revision
                  OR task.assigned_membership_id IS DISTINCT FROM NEW.assigned_membership_id THEN
                  RAISE EXCEPTION 'Exception requires assigned unstarted/blocked task' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO pred FROM app.execution_tasks WHERE id=NEW.predecessor_task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR pred.status<>'SKIPPED'
                  OR NOT EXISTS(SELECT 1 FROM app.playbook_step_dependencies d WHERE d.id=NEW.dependency_id AND d.version_id=NEW.version_id AND d.predecessor_id=pred.step_id AND d.successor_id=task.step_id)
                  OR NOT EXISTS(SELECT 1 FROM app.skip_authorizations s WHERE s.id=NEW.skip_authorization_id AND s.action='APPROVED' AND s.task_id=pred.id AND s.execution_id=NEW.execution_id AND s.organization_id=NEW.organization_id AND s.incident_id=NEW.incident_id AND s.version_id=NEW.version_id) THEN
                  RAISE EXCEPTION 'Exception requires an actual edge and authorized skipped predecessor' USING ERRCODE='23514';
                 END IF;
                 SELECT m.user_id INTO assignee FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                  AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP);
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users u WHERE u.id=NEW.actor_user_id AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Exception participants must be eligible' USING ERRCODE='23514';
                 END IF;
                 IF NEW.dependency_context_json IS DISTINCT FROM app.dependency_exception_context(NEW.execution_id,NEW.task_id) THEN
                  RAISE EXCEPTION 'Exception context is stale or forged' USING ERRCODE='23514';
                 END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF NEW.actor_user_id<>assignee THEN RAISE EXCEPTION 'Only assignee may request exception' USING ERRCODE='23514'; END IF;
                 ELSE
                  SELECT * INTO req FROM app.dependency_exceptions WHERE id=NEW.request_id AND action='REQUESTED' AND task_id=NEW.task_id AND predecessor_task_id=NEW.predecessor_task_id;
                  IF NOT FOUND OR req.execution_revision<>NEW.execution_revision-1 OR req.incident_revision<>NEW.incident_revision-1
                   OR NEW.impact<>req.impact OR NEW.residual_risk<>req.residual_risk OR NEW.dependency_context_json<>req.dependency_context_json
                   OR EXISTS(SELECT 1 FROM app.dependency_exceptions d WHERE d.request_id=req.id AND d.id<>NEW.id)
                   OR EXISTS(SELECT 1 FROM app.dependency_exceptions r WHERE r.task_id=NEW.task_id AND r.predecessor_task_id=NEW.predecessor_task_id AND r.action='REQUESTED' AND r.execution_revision>req.execution_revision)
                   OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN')
                   OR NEW.self_approval IS DISTINCT FROM (NEW.action='APPROVED' AND NEW.actor_user_id=req.actor_user_id) THEN
                   RAISE EXCEPTION 'Exception decision requires current request and explicit admin decision' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id AND h.version_id=NEW.version_id
                   AND h.execution_id=NEW.execution_id AND h.task_id=NEW.task_id AND h.revision=NEW.execution_revision AND h.incident_revision=NEW.incident_revision
                   AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.assigned_membership_id=NEW.assigned_membership_id
                   AND h.task_status=task.status AND h.action='DEPENDENCY_EXCEPTION_'||NEW.action)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action='DEPENDENCY_EXCEPTION_'||NEW.action) THEN
                  RAISE EXCEPTION 'Exception requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid, snapshot jsonb)
                 RETURNS boolean
                 LANGUAGE sql
                 STABLE
                AS $function$
                SELECT EXISTS(
                SELECT 1 FROM app.dependency_exceptions r JOIN app.dependency_exceptions d ON d.request_id=r.id AND d.action='APPROVED'
                JOIN app.execution_tasks t ON t.id=r.task_id AND t.execution_id=r.execution_id
                JOIN app.execution_tasks p ON p.id=r.predecessor_task_id AND p.execution_id=r.execution_id AND p.status='SKIPPED'
                JOIN app.playbook_executions e ON e.id=r.execution_id AND e.status='IN_PROGRESS'
                JOIN app.playbook_versions v ON v.id=e.version_id AND app.execution_version_usable(e.id)
                JOIN app.organizations o ON o.id=e.organization_id AND o.status='ACTIVE'
                JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id AND i.status='IN_RESPONSE'
                JOIN app.skip_authorizations s ON s.id=r.skip_authorization_id AND s.task_id=p.id AND s.action='APPROVED'
                WHERE r.task_id=target AND r.predecessor_task_id=predecessor AND r.action='REQUESTED'
                AND NOT EXISTS(SELECT 1 FROM app.dependency_exception_revocations x WHERE x.approval_id=d.id)
                AND r.assigned_membership_id=assigned AND t.assigned_membership_id=assigned AND r.dependency_context_json=snapshot
                AND EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                WHERE m.id=assigned AND m.organization_id=r.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP))
                AND NOT EXISTS(SELECT 1 FROM app.dependency_exceptions newer WHERE newer.task_id=target AND newer.predecessor_task_id=predecessor
                AND newer.action='REQUESTED' AND newer.execution_revision>r.execution_revision)
                AND NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.task_id=target AND h.action='EXECUTION_TASK_ASSIGNED' AND h.revision>r.execution_revision)
                );
                $function$;
                
                CREATE OR REPLACE FUNCTION app.check_dependency_revocation()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; run app.playbook_executions%ROWTYPE; parent app.incidents%ROWTYPE;
                BEGIN
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND organization_id=NEW.organization_id
                   AND incident_id=NEW.incident_id AND version_id=NEW.version_id AND execution_id=NEW.execution_id;
                 IF NOT FOUND OR task.status NOT IN ('PENDING','BLOCKED') THEN
                  RAISE EXCEPTION 'Revocation requires an unfinished task; running tasks must be blocked atomically' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.execution_id AND organization_id=NEW.organization_id
                   AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR run.status<>'IN_PROGRESS' OR run.revision<>NEW.execution_revision THEN
                  RAISE EXCEPTION 'Revocation requires current execution revision' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO parent FROM app.incidents WHERE id=NEW.incident_id AND organization_id=NEW.organization_id;
                 IF NOT FOUND OR parent.status<>'IN_RESPONSE' OR parent.revision<>NEW.incident_revision
                  OR NOT app.execution_version_usable(NEW.execution_id)
                  OR NOT EXISTS(SELECT 1 FROM app.organizations WHERE id=NEW.organization_id AND status='ACTIVE') THEN
                  RAISE EXCEPTION 'Revocation requires active incident, organization and usable version' USING ERRCODE='23514';
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.dependency_exceptions a WHERE a.id=NEW.approval_id AND a.action='APPROVED'
                   AND a.execution_revision<NEW.execution_revision AND a.incident_revision<NEW.incident_revision)
                  OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                    WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN'
                     AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Revocation requires an approved exception and eligible organization admin' USING ERRCODE='23514';
                 END IF;
                 IF NEW.blocked_task AND (task.status<>'BLOCKED' OR task.changed_at_revision<>NEW.execution_revision) THEN
                  RAISE EXCEPTION 'Revocation blocking must be atomic' USING ERRCODE='23514';
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.version_id=NEW.version_id AND h.execution_id=NEW.execution_id AND h.task_id=NEW.task_id AND h.revision=NEW.execution_revision
                   AND h.incident_revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id
                   AND h.action='DEPENDENCY_EXCEPTION_REVOKED' AND h.reason=NEW.reason AND h.execution_status='IN_PROGRESS'
                   AND h.task_status=task.status AND h.assigned_membership_id IS NOT DISTINCT FROM task.assigned_membership_id)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id
                   AND h.action='DEPENDENCY_EXCEPTION_REVOKED' AND h.reason=NEW.reason) THEN
                  RAISE EXCEPTION 'Revocation requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.protect_execution_header()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
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
                 IF NEW.status='COMPLETED' AND NOT app.execution_version_usable(NEW.id) THEN
                  RAISE EXCEPTION 'Withdrawn execution cannot complete' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                CREATE FUNCTION app.check_security_hold() RETURNS trigger LANGUAGE plpgsql AS $b$
                DECLARE scope uuid;
                BEGIN
                 SELECT p.organization_id INTO scope FROM app.playbooks p JOIN app.playbook_versions v ON v.playbook_id=p.id
                 WHERE p.id=NEW.playbook_id AND p.revision=NEW.revision AND v.id=NEW.version_id AND v.status='ARCHIVED';
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users WHERE id=NEW.actor_user_id AND is_active AND (lockout_end IS NULL OR lockout_end<=CURRENT_TIMESTAMP)) THEN
                 RAISE EXCEPTION 'Security withdrawal requires current archived source and eligible actor' USING ERRCODE='23514'; END IF;
                 IF (scope IS NULL AND NOT EXISTS(SELECT 1 FROM app.platform_administrators WHERE user_id=NEW.actor_user_id AND revoked_at IS NULL))
                 OR (scope IS NOT NULL AND NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN app.organizations o ON o.id=m.organization_id
                 WHERE m.organization_id=scope AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN' AND o.status='ACTIVE')) THEN
                 RAISE EXCEPTION 'Security withdrawal requires source administrator' USING ERRCODE='23514'; END IF;
                 IF NOT EXISTS(SELECT 1 FROM audit.audit_events WHERE actor_user_id=NEW.actor_user_id AND correlation_id=NEW.correlation_id AND resource_id=NEW.playbook_id
                 AND organization_id IS NOT DISTINCT FROM scope AND action='playbook.security_withdrawn' AND outcome='SUCCESS') THEN
                 RAISE EXCEPTION 'Security withdrawal requires atomic audit' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END;$b$;
                CREATE FUNCTION app.check_security_decision() RETURNS trigger LANGUAGE plpgsql AS $b$
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
                CREATE FUNCTION app.check_security_execution() RETURNS trigger LANGUAGE plpgsql AS $b$
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
                CREATE CONSTRAINT TRIGGER check_security_execution AFTER UPDATE ON app.playbook_executions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_security_execution();
                CREATE TRIGGER protect_playbook_security_holds BEFORE INSERT OR UPDATE OR DELETE ON app.playbook_security_holds FOR EACH ROW EXECUTE FUNCTION app.protect_security_history();
                CREATE TRIGGER truncate_playbook_security_holds BEFORE TRUNCATE ON app.playbook_security_holds FOR EACH STATEMENT EXECUTE FUNCTION app.protect_security_history();
                CREATE CONSTRAINT TRIGGER check_security_hold AFTER INSERT ON app.playbook_security_holds DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_security_hold();
                CREATE TRIGGER protect_execution_security_decisions BEFORE INSERT OR UPDATE OR DELETE ON app.execution_security_decisions FOR EACH ROW EXECUTE FUNCTION app.protect_security_history();
                CREATE TRIGGER truncate_execution_security_decisions BEFORE TRUNCATE ON app.execution_security_decisions FOR EACH STATEMENT EXECUTE FUNCTION app.protect_security_history();
                CREATE CONSTRAINT TRIGGER check_security_decision AFTER INSERT ON app.execution_security_decisions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_security_decision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $g$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.playbook_security_holds) OR EXISTS(SELECT 1 FROM app.execution_security_decisions)
                 OR EXISTS(SELECT 1 FROM app.incident_events WHERE action IN ('SECURITY_CONTINUITY_AUTHORIZED','SECURITY_EXECUTION_CANCELLED')) THEN
                 RAISE EXCEPTION 'Cannot remove security hold history' USING ERRCODE='23514'; END IF;
                END;$g$;
                DROP TRIGGER check_security_execution ON app.playbook_executions;
                DROP FUNCTION app.check_security_execution();
                DROP TRIGGER protect_playbook_security_holds ON app.playbook_security_holds;
                DROP TRIGGER truncate_playbook_security_holds ON app.playbook_security_holds;
                DROP TRIGGER check_security_hold ON app.playbook_security_holds;
                DROP FUNCTION app.check_security_hold();
                DROP TRIGGER protect_execution_security_decisions ON app.execution_security_decisions;
                DROP TRIGGER truncate_execution_security_decisions ON app.execution_security_decisions;
                DROP TRIGGER check_security_decision ON app.execution_security_decisions;
                DROP FUNCTION app.check_security_decision();
                CREATE OR REPLACE FUNCTION app.protect_step_approval()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
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
                                 IF NOT app.execution_dependencies_satisfied(NEW.execution_id,NEW.task_id,NEW.assigned_membership_id) THEN
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
                                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.protect_execution_task()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                                                                                DECLARE run_status text; version_status text; incident_status text; approval boolean; evidence boolean;
                                                                                BEGIN
                                                                                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Execution tasks cannot be physically removed' USING ERRCODE='42501'; END IF;
                                                                                 IF TG_OP='UPDATE' THEN
                                                                                  IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                                                                                    OR NEW.incident_id IS DISTINCT FROM OLD.incident_id OR NEW.execution_id IS DISTINCT FROM OLD.execution_id
                                                                                    OR NEW.version_id IS DISTINCT FROM OLD.version_id OR NEW.step_id IS DISTINCT FROM OLD.step_id
                                                                                    OR NEW.changed_at_revision <= OLD.changed_at_revision OR OLD.status IN ('COMPLETED','SKIPPED') THEN
                                                                                   RAISE EXCEPTION 'Execution task identity and completed state are immutable' USING ERRCODE='23514';
                                                                                  END IF;
                                                                                  IF NEW.status <> OLD.status AND NOT ((OLD.status='PENDING' AND NEW.status IN ('IN_PROGRESS','BLOCKED','SKIPPED'))
                                                                                     OR (OLD.status='BLOCKED' AND NEW.status IN ('PENDING','IN_PROGRESS','SKIPPED'))
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
                                                                                  IF NOT app.execution_dependencies_satisfied(NEW.execution_id,NEW.id,NEW.assigned_membership_id) THEN
                                                                                   RAISE EXCEPTION 'Task dependencies must be completed' USING ERRCODE='23514';
                                                                                  END IF;
                                                                                 END IF;
                                                                 IF TG_OP='UPDATE' AND NEW.status='SKIPPED' AND NEW.assigned_membership_id IS DISTINCT FROM OLD.assigned_membership_id THEN
                                                  RAISE EXCEPTION 'Skip cannot change assignee' USING ERRCODE='23514';
                                                 END IF;
                                                 IF NEW.completion_evidence_id IS NOT NULL THEN
                                                                  IF NEW.status<>'COMPLETED' THEN RAISE EXCEPTION 'Evidence can only be selected on completion' USING ERRCODE='23514'; END IF;
                                                                  PERFORM id FROM app.incident_evidence WHERE id=NEW.completion_evidence_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id
                                                                   AND version_id=NEW.version_id AND execution_id=NEW.execution_id AND task_id=NEW.id AND status='ACTIVE' AND (kind<>'FILE' OR EXISTS(SELECT 1 FROM app.evidence_files f WHERE f.evidence_id=app.incident_evidence.id AND f.state='AVAILABLE')) FOR SHARE;
                                                                  IF NOT FOUND THEN RAISE EXCEPTION 'Completion requires active evidence of this exact task' USING ERRCODE='23514'; END IF;
                                                                 END IF;
                                                                 RETURN NEW;
                                                                                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.check_skip_authorization()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; req app.skip_authorizations%ROWTYPE; assignee uuid;
                BEGIN
                 PERFORM e.id FROM app.playbook_executions e JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                  JOIN app.playbook_versions v ON v.id=e.version_id JOIN app.organizations o ON o.id=e.organization_id
                  WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id
                   AND e.status='IN_PROGRESS' AND i.status='IN_RESPONSE' AND v.status='PUBLISHED' AND o.status='ACTIVE'
                   AND e.revision=NEW.execution_revision AND i.revision=NEW.incident_revision FOR UPDATE OF e;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Skip requires current active execution and incident' USING ERRCODE='23514'; END IF;
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id FOR UPDATE;
                 IF NOT FOUND OR task.assigned_membership_id IS DISTINCT FROM NEW.assigned_membership_id
                  OR (NEW.action='APPROVED' AND (task.status<>'SKIPPED' OR task.changed_at_revision<>NEW.execution_revision))
                  OR (NEW.action<>'APPROVED' AND (task.status NOT IN ('PENDING','BLOCKED') OR task.changed_at_revision>=NEW.execution_revision)) THEN
                  RAISE EXCEPTION 'Skip requires matching assigned task and state' USING ERRCODE='23514';
                 END IF;
                 SELECT m.user_id INTO assignee FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE'
                  AND m.role IN ('ORG_ADMIN','ANALYST') AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP);
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users u WHERE u.id=NEW.actor_user_id AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Skip participants must be eligible' USING ERRCODE='23514';
                 END IF;
                 IF NEW.dependency_context_json IS DISTINCT FROM app.skip_dependency_context(NEW.execution_id, NEW.task_id) THEN
                  RAISE EXCEPTION 'Skip dependency context mismatch' USING ERRCODE='23514';
                 END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF NEW.actor_user_id<>assignee OR EXISTS(SELECT 1 FROM app.skip_authorizations r WHERE r.task_id=NEW.task_id AND r.action='REQUESTED'
                    AND r.execution_revision=NEW.execution_revision-1 AND r.incident_revision=NEW.incident_revision-1
                    AND NOT EXISTS(SELECT 1 FROM app.skip_authorizations d WHERE d.request_id=r.id AND d.action='REJECTED')) THEN
                   RAISE EXCEPTION 'Only assignee may request fresh skip authorization' USING ERRCODE='23514';
                  END IF;
                 ELSE
                  SELECT * INTO req FROM app.skip_authorizations WHERE id=NEW.request_id AND action='REQUESTED' AND task_id=NEW.task_id;
                  IF NOT FOUND OR req.execution_revision<>NEW.execution_revision-1 OR req.incident_revision<>NEW.incident_revision-1
                   OR NEW.impact<>req.impact OR NEW.residual_risk<>req.residual_risk OR NEW.dependency_context_json<>req.dependency_context_json
                   OR EXISTS(SELECT 1 FROM app.skip_authorizations d WHERE d.request_id=req.id AND d.id<>NEW.id)
                   OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN')
                   OR NEW.self_approval IS DISTINCT FROM (NEW.action='APPROVED' AND NEW.actor_user_id=req.actor_user_id) THEN
                   RAISE EXCEPTION 'Skip decision requires current request and explicit admin decision' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events e WHERE e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id
                  AND e.version_id=NEW.version_id AND e.execution_id=NEW.execution_id AND e.task_id=NEW.task_id AND e.revision=NEW.execution_revision
                  AND e.incident_revision=NEW.incident_revision AND e.actor_user_id=NEW.actor_user_id AND e.correlation_id=NEW.correlation_id
                  AND e.assigned_membership_id=NEW.assigned_membership_id AND e.action='SKIP_AUTHORIZATION_'||NEW.action AND e.task_status=task.status)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events i WHERE i.organization_id=NEW.organization_id AND i.incident_id=NEW.incident_id
                    AND i.revision=NEW.incident_revision AND i.actor_user_id=NEW.actor_user_id AND i.correlation_id=NEW.correlation_id
                    AND i.action='SKIP_AUTHORIZATION_'||NEW.action) THEN
                  RAISE EXCEPTION 'Skip requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.check_dependency_exception()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; pred app.execution_tasks%ROWTYPE; req app.dependency_exceptions%ROWTYPE; assignee uuid;
                BEGIN
                 PERFORM e.id FROM app.playbook_executions e JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                  JOIN app.playbook_versions v ON v.id=e.version_id JOIN app.organizations o ON o.id=e.organization_id
                 WHERE e.id=NEW.execution_id AND e.organization_id=NEW.organization_id AND e.incident_id=NEW.incident_id AND e.version_id=NEW.version_id
                  AND e.status='IN_PROGRESS' AND i.status='IN_RESPONSE' AND v.status='PUBLISHED' AND o.status='ACTIVE'
                  AND e.revision=NEW.execution_revision AND i.revision=NEW.incident_revision FOR UPDATE OF e;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Exception requires current usable execution and incident' USING ERRCODE='23514'; END IF;
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id FOR UPDATE;
                 IF NOT FOUND OR task.status NOT IN ('PENDING','BLOCKED') OR task.changed_at_revision>=NEW.execution_revision
                  OR task.assigned_membership_id IS DISTINCT FROM NEW.assigned_membership_id THEN
                  RAISE EXCEPTION 'Exception requires assigned unstarted/blocked task' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO pred FROM app.execution_tasks WHERE id=NEW.predecessor_task_id AND execution_id=NEW.execution_id AND organization_id=NEW.organization_id AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR pred.status<>'SKIPPED'
                  OR NOT EXISTS(SELECT 1 FROM app.playbook_step_dependencies d WHERE d.id=NEW.dependency_id AND d.version_id=NEW.version_id AND d.predecessor_id=pred.step_id AND d.successor_id=task.step_id)
                  OR NOT EXISTS(SELECT 1 FROM app.skip_authorizations s WHERE s.id=NEW.skip_authorization_id AND s.action='APPROVED' AND s.task_id=pred.id AND s.execution_id=NEW.execution_id AND s.organization_id=NEW.organization_id AND s.incident_id=NEW.incident_id AND s.version_id=NEW.version_id) THEN
                  RAISE EXCEPTION 'Exception requires an actual edge and authorized skipped predecessor' USING ERRCODE='23514';
                 END IF;
                 SELECT m.user_id INTO assignee FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                 WHERE m.id=NEW.assigned_membership_id AND m.organization_id=NEW.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                  AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP);
                 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM auth.asp_net_users u WHERE u.id=NEW.actor_user_id AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Exception participants must be eligible' USING ERRCODE='23514';
                 END IF;
                 IF NEW.dependency_context_json IS DISTINCT FROM app.dependency_exception_context(NEW.execution_id,NEW.task_id) THEN
                  RAISE EXCEPTION 'Exception context is stale or forged' USING ERRCODE='23514';
                 END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF NEW.actor_user_id<>assignee THEN RAISE EXCEPTION 'Only assignee may request exception' USING ERRCODE='23514'; END IF;
                 ELSE
                  SELECT * INTO req FROM app.dependency_exceptions WHERE id=NEW.request_id AND action='REQUESTED' AND task_id=NEW.task_id AND predecessor_task_id=NEW.predecessor_task_id;
                  IF NOT FOUND OR req.execution_revision<>NEW.execution_revision-1 OR req.incident_revision<>NEW.incident_revision-1
                   OR NEW.impact<>req.impact OR NEW.residual_risk<>req.residual_risk OR NEW.dependency_context_json<>req.dependency_context_json
                   OR EXISTS(SELECT 1 FROM app.dependency_exceptions d WHERE d.request_id=req.id AND d.id<>NEW.id)
                   OR EXISTS(SELECT 1 FROM app.dependency_exceptions r WHERE r.task_id=NEW.task_id AND r.predecessor_task_id=NEW.predecessor_task_id AND r.action='REQUESTED' AND r.execution_revision>req.execution_revision)
                   OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN')
                   OR NEW.self_approval IS DISTINCT FROM (NEW.action='APPROVED' AND NEW.actor_user_id=req.actor_user_id) THEN
                   RAISE EXCEPTION 'Exception decision requires current request and explicit admin decision' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id AND h.version_id=NEW.version_id
                   AND h.execution_id=NEW.execution_id AND h.task_id=NEW.task_id AND h.revision=NEW.execution_revision AND h.incident_revision=NEW.incident_revision
                   AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.assigned_membership_id=NEW.assigned_membership_id
                   AND h.task_status=task.status AND h.action='DEPENDENCY_EXCEPTION_'||NEW.action)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id AND h.action='DEPENDENCY_EXCEPTION_'||NEW.action) THEN
                  RAISE EXCEPTION 'Exception requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid, snapshot jsonb)
                 RETURNS boolean
                 LANGUAGE sql
                 STABLE
                AS $function$
                SELECT EXISTS(
                SELECT 1 FROM app.dependency_exceptions r JOIN app.dependency_exceptions d ON d.request_id=r.id AND d.action='APPROVED'
                JOIN app.execution_tasks t ON t.id=r.task_id AND t.execution_id=r.execution_id
                JOIN app.execution_tasks p ON p.id=r.predecessor_task_id AND p.execution_id=r.execution_id AND p.status='SKIPPED'
                JOIN app.playbook_executions e ON e.id=r.execution_id AND e.status='IN_PROGRESS'
                JOIN app.playbook_versions v ON v.id=e.version_id AND v.status='PUBLISHED'
                JOIN app.organizations o ON o.id=e.organization_id AND o.status='ACTIVE'
                JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id AND i.status='IN_RESPONSE'
                JOIN app.skip_authorizations s ON s.id=r.skip_authorization_id AND s.task_id=p.id AND s.action='APPROVED'
                WHERE r.task_id=target AND r.predecessor_task_id=predecessor AND r.action='REQUESTED'
                AND NOT EXISTS(SELECT 1 FROM app.dependency_exception_revocations x WHERE x.approval_id=d.id)
                AND r.assigned_membership_id=assigned AND t.assigned_membership_id=assigned AND r.dependency_context_json=snapshot
                AND EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                WHERE m.id=assigned AND m.organization_id=r.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP))
                AND NOT EXISTS(SELECT 1 FROM app.dependency_exceptions newer WHERE newer.task_id=target AND newer.predecessor_task_id=predecessor
                AND newer.action='REQUESTED' AND newer.execution_revision>r.execution_revision)
                AND NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.task_id=target AND h.action='EXECUTION_TASK_ASSIGNED' AND h.revision>r.execution_revision)
                );
                $function$;
                
                CREATE OR REPLACE FUNCTION app.check_dependency_revocation()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                DECLARE task app.execution_tasks%ROWTYPE; run app.playbook_executions%ROWTYPE; parent app.incidents%ROWTYPE;
                BEGIN
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.task_id AND organization_id=NEW.organization_id
                   AND incident_id=NEW.incident_id AND version_id=NEW.version_id AND execution_id=NEW.execution_id;
                 IF NOT FOUND OR task.status NOT IN ('PENDING','BLOCKED') THEN
                  RAISE EXCEPTION 'Revocation requires an unfinished task; running tasks must be blocked atomically' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO run FROM app.playbook_executions WHERE id=NEW.execution_id AND organization_id=NEW.organization_id
                   AND incident_id=NEW.incident_id AND version_id=NEW.version_id;
                 IF NOT FOUND OR run.status<>'IN_PROGRESS' OR run.revision<>NEW.execution_revision THEN
                  RAISE EXCEPTION 'Revocation requires current execution revision' USING ERRCODE='23514';
                 END IF;
                 SELECT * INTO parent FROM app.incidents WHERE id=NEW.incident_id AND organization_id=NEW.organization_id;
                 IF NOT FOUND OR parent.status<>'IN_RESPONSE' OR parent.revision<>NEW.incident_revision
                  OR NOT EXISTS(SELECT 1 FROM app.playbook_versions WHERE id=NEW.version_id AND status='PUBLISHED')
                  OR NOT EXISTS(SELECT 1 FROM app.organizations WHERE id=NEW.organization_id AND status='ACTIVE') THEN
                  RAISE EXCEPTION 'Revocation requires active incident, organization and usable version' USING ERRCODE='23514';
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.dependency_exceptions a WHERE a.id=NEW.approval_id AND a.action='APPROVED'
                   AND a.execution_revision<NEW.execution_revision AND a.incident_revision<NEW.incident_revision)
                  OR NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                    WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.actor_user_id AND m.status='ACTIVE' AND m.role='ORG_ADMIN'
                     AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP)) THEN
                  RAISE EXCEPTION 'Revocation requires an approved exception and eligible organization admin' USING ERRCODE='23514';
                 END IF;
                 IF NEW.blocked_task AND (task.status<>'BLOCKED' OR task.changed_at_revision<>NEW.execution_revision) THEN
                  RAISE EXCEPTION 'Revocation blocking must be atomic' USING ERRCODE='23514';
                 END IF;
                 IF NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.version_id=NEW.version_id AND h.execution_id=NEW.execution_id AND h.task_id=NEW.task_id AND h.revision=NEW.execution_revision
                   AND h.incident_revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id
                   AND h.action='DEPENDENCY_EXCEPTION_REVOKED' AND h.reason=NEW.reason AND h.execution_status='IN_PROGRESS'
                   AND h.task_status=task.status AND h.assigned_membership_id IS NOT DISTINCT FROM task.assigned_membership_id)
                  OR NOT EXISTS(SELECT 1 FROM app.incident_events h WHERE h.organization_id=NEW.organization_id AND h.incident_id=NEW.incident_id
                   AND h.revision=NEW.incident_revision AND h.actor_user_id=NEW.actor_user_id AND h.correlation_id=NEW.correlation_id
                   AND h.action='DEPENDENCY_EXCEPTION_REVOKED' AND h.reason=NEW.reason) THEN
                  RAISE EXCEPTION 'Revocation requires matching atomic history' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $function$;
                
                CREATE OR REPLACE FUNCTION app.protect_execution_header()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
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
                END; $function$;
                
                DROP FUNCTION app.execution_version_usable(uuid);
                DROP FUNCTION app.protect_security_history();
                """);

            migrationBuilder.DropTable(
                name: "execution_security_decisions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbook_security_holds",
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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','DEPENDENCY_EXCEPTION_REVOKED'))");
        }
    }
}
