using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkipAuthorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_task_status",
                schema: "app",
                table: "execution_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events");

            migrationBuilder.CreateTable(
                name: "skip_authorizations",
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
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    impact = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    residual_risk = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    dependency_context_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skip_authorizations", x => x.id);
                    table.UniqueConstraint("ak_skip_authorization_scope", x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.assigned_membership_id, x.id });
                    table.CheckConstraint("ck_skip_authorization_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                    table.CheckConstraint("ck_skip_authorization_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
                    table.CheckConstraint("ck_skip_authorization_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>'' AND btrim(impact)<>'' AND btrim(residual_risk)<>'' AND jsonb_typeof(dependency_context_json)='object' AND octet_length(dependency_context_json::text)<=65536");
                    table.ForeignKey(
                        name: "fk_skip_authorizations_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_skip_authorizations_execution_tasks_organization_id_inciden",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_skip_authorizations_organization_memberships_organization_i",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_skip_authorizations_skip_authorizations_organization_id_inc",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.assigned_membership_id, x.request_id },
                        principalSchema: "app",
                        principalTable: "skip_authorizations",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "assigned_membership_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_task_status",
                schema: "app",
                table: "execution_tasks",
                sql: "status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED'))");

            migrationBuilder.CreateIndex(
                name: "ix_skip_authorizations_actor_user_id",
                schema: "app",
                table: "skip_authorizations",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_skip_authorizations_organization_id_assigned_membership_id",
                schema: "app",
                table: "skip_authorizations",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_skip_authorizations_organization_id_incident_id_version_id_",
                schema: "app",
                table: "skip_authorizations",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "assigned_membership_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_skip_authorizations_request_id",
                schema: "app",
                table: "skip_authorizations",
                column: "request_id",
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_skip_authorizations_task_id_execution_revision",
                schema: "app",
                table: "skip_authorizations",
                columns: new[] { "task_id", "execution_revision" },
                unique: true);
            migrationBuilder.Sql("""
                
                CREATE FUNCTION app.skip_dependency_context(run uuid, target uuid) RETURNS jsonb LANGUAGE sql STABLE AS $body$
                 SELECT jsonb_build_object(
                  'predecessors', COALESCE((SELECT jsonb_agg(jsonb_build_object('taskId',p.id,'status',p.status) ORDER BY p.id)
                    FROM app.execution_tasks t JOIN app.playbook_step_dependencies d ON d.version_id=t.version_id AND d.successor_id=t.step_id
                    JOIN app.execution_tasks p ON p.execution_id=t.execution_id AND p.step_id=d.predecessor_id
                    WHERE t.execution_id=run AND t.id=target),'[]'::jsonb),
                  'successors', COALESCE((SELECT jsonb_agg(jsonb_build_object('taskId',s.id,'status',s.status) ORDER BY s.id)
                    FROM app.execution_tasks t JOIN app.playbook_step_dependencies d ON d.version_id=t.version_id AND d.predecessor_id=t.step_id
                    JOIN app.execution_tasks s ON s.execution_id=t.execution_id AND s.step_id=d.successor_id
                    WHERE t.execution_id=run AND t.id=target),'[]'::jsonb));
                $body$;
                CREATE FUNCTION app.protect_skip_authorization() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Skip authorizations are append-only' USING ERRCODE='42501'; END; $body$;
                CREATE TRIGGER protect_skip_authorization BEFORE UPDATE OR DELETE ON app.skip_authorizations FOR EACH ROW EXECUTE FUNCTION app.protect_skip_authorization();
                CREATE TRIGGER protect_skip_authorization_truncate BEFORE TRUNCATE ON app.skip_authorizations FOR EACH STATEMENT EXECUTE FUNCTION app.protect_skip_authorization();
                
                CREATE FUNCTION app.check_skip_authorization() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_skip_authorization AFTER INSERT ON app.skip_authorizations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_skip_authorization();
                
                CREATE FUNCTION app.check_task_skip() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF NEW.status='SKIPPED' AND NOT EXISTS(SELECT 1 FROM app.skip_authorizations a WHERE a.task_id=NEW.id AND a.execution_id=NEW.execution_id
                  AND a.organization_id=NEW.organization_id AND a.incident_id=NEW.incident_id AND a.version_id=NEW.version_id
                  AND a.assigned_membership_id=NEW.assigned_membership_id AND a.action='APPROVED' AND a.execution_revision=NEW.changed_at_revision) THEN
                  RAISE EXCEPTION 'Skipped task requires atomic authorization' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_task_skip AFTER INSERT OR UPDATE ON app.execution_tasks DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_task_skip();
                CREATE OR REPLACE FUNCTION app.protect_execution_task() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                                                                  IF EXISTS (SELECT 1 FROM app.playbook_step_dependencies d WHERE d.version_id=NEW.version_id AND d.successor_id=NEW.step_id
                                                                      AND NOT EXISTS (SELECT 1 FROM app.execution_tasks t WHERE t.execution_id=NEW.execution_id AND t.step_id=d.predecessor_id AND t.status='COMPLETED')) THEN
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
                                                                END; $body$;
                CREATE OR REPLACE FUNCTION app.check_execution_integrity() RETURNS trigger LANGUAGE plpgsql AS $body$
                                DECLARE target uuid; source_version uuid; run_status text; run_revision bigint;
                                BEGIN
                                 IF TG_TABLE_NAME='execution_tasks' THEN target=NEW.execution_id; ELSE target=NEW.id; END IF;
                                 SELECT version_id,status,revision INTO source_version,run_status,run_revision FROM app.playbook_executions WHERE id=target;
                                 IF NOT FOUND THEN RETURN NEW; END IF;
                                 IF (SELECT count(*) FROM app.execution_tasks WHERE execution_id=target) <>
                                    (SELECT count(*) FROM app.playbook_steps WHERE version_id=source_version)
                                    OR NOT EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target)
                                    OR EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target AND changed_at_revision > run_revision)
                                    OR (run_status='COMPLETED' AND EXISTS(SELECT 1 FROM app.execution_tasks WHERE execution_id=target AND status NOT IN ('COMPLETED','SKIPPED'))) THEN
                                  RAISE EXCEPTION 'Execution must contain exactly its version tasks and completion must be complete' USING ERRCODE='23514';
                                 END IF;
                                 RETURN NEW;
                                END; $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.skip_authorizations) OR EXISTS(SELECT 1 FROM app.execution_tasks WHERE status='SKIPPED')
                  OR EXISTS(SELECT 1 FROM app.incident_events WHERE action LIKE 'SKIP_AUTHORIZATION_%') THEN
                  RAISE EXCEPTION 'Cannot roll back skip authorizations while data or history exists'; END IF;
                 END; $guard$;
                 DROP TRIGGER check_task_skip ON app.execution_tasks;
                 DROP FUNCTION app.check_task_skip();
                 DROP FUNCTION app.skip_dependency_context(uuid,uuid);
                 DROP TRIGGER check_skip_authorization ON app.skip_authorizations;
                 DROP FUNCTION app.check_skip_authorization();
                 DROP TRIGGER protect_skip_authorization ON app.skip_authorizations;
                 DROP TRIGGER protect_skip_authorization_truncate ON app.skip_authorizations;
                 DROP FUNCTION app.protect_skip_authorization();
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
                CREATE OR REPLACE FUNCTION app.check_execution_integrity() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                """);
            migrationBuilder.DropTable(
                name: "skip_authorizations",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_task_status",
                schema: "app",
                table: "execution_tasks");

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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_task_status",
                schema: "app",
                table: "execution_tasks",
                sql: "status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED')");

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
        }
    }
}
