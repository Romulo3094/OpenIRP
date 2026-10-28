using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DependencyExceptionRevocations : Migration
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
                name: "dependency_exception_revocations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skip_authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_revision = table.Column<long>(type: "bigint", nullable: false),
                    incident_revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    blocked_task = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dependency_exception_revocations", x => x.id);
                    table.CheckConstraint("ck_dependency_revocation_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>''");
                    table.ForeignKey(
                        name: "fk_dependency_exception_revocations_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exception_revocations_dependency_exceptions_orga",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.dependency_id, x.predecessor_task_id, x.skip_authorization_id, x.assigned_membership_id, x.approval_id },
                        principalSchema: "app",
                        principalTable: "dependency_exceptions",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "dependency_id", "predecessor_task_id", "skip_authorization_id", "assigned_membership_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exception_revocations_actor_user_id",
                schema: "app",
                table: "dependency_exception_revocations",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exception_revocations_approval_id",
                schema: "app",
                table: "dependency_exception_revocations",
                column: "approval_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exception_revocations_organization_id_incident_i",
                schema: "app",
                table: "dependency_exception_revocations",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "dependency_id", "predecessor_task_id", "skip_authorization_id", "assigned_membership_id", "approval_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exception_revocations_task_id_execution_revision",
                schema: "app",
                table: "dependency_exception_revocations",
                columns: new[] { "task_id", "execution_revision" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid, snapshot jsonb) RETURNS boolean LANGUAGE sql STABLE AS $body$
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
                $body$;
                CREATE FUNCTION app.protect_dependency_revocation() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Dependency exception revocations are append-only' USING ERRCODE='42501'; END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_dependency_revocation BEFORE INSERT OR UPDATE OR DELETE ON app.dependency_exception_revocations
                 FOR EACH ROW EXECUTE FUNCTION app.protect_dependency_revocation();
                CREATE TRIGGER protect_dependency_revocation_truncate BEFORE TRUNCATE ON app.dependency_exception_revocations
                 FOR EACH STATEMENT EXECUTE FUNCTION app.protect_dependency_revocation();
                CREATE FUNCTION app.check_dependency_revocation() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_dependency_revocation AFTER INSERT ON app.dependency_exception_revocations
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_dependency_revocation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.dependency_exception_revocations)
                  OR EXISTS(SELECT 1 FROM app.incident_events WHERE action='DEPENDENCY_EXCEPTION_REVOKED') THEN
                  RAISE EXCEPTION 'Cannot remove dependency revocation history' USING ERRCODE='23514';
                 END IF;
                END; $guard$;
                DROP TRIGGER check_dependency_revocation ON app.dependency_exception_revocations;
                DROP TRIGGER protect_dependency_revocation ON app.dependency_exception_revocations;
                DROP TRIGGER protect_dependency_revocation_truncate ON app.dependency_exception_revocations;
                DROP FUNCTION app.check_dependency_revocation();
                DROP FUNCTION app.protect_dependency_revocation();
                CREATE OR REPLACE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid, snapshot jsonb) RETURNS boolean LANGUAGE sql STABLE AS $body$
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
                AND r.assigned_membership_id=assigned AND t.assigned_membership_id=assigned AND r.dependency_context_json=snapshot
                AND EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id
                WHERE m.id=assigned AND m.organization_id=r.organization_id AND m.status='ACTIVE' AND m.role IN ('ORG_ADMIN','ANALYST')
                AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=CURRENT_TIMESTAMP))
                AND NOT EXISTS(SELECT 1 FROM app.dependency_exceptions newer WHERE newer.task_id=target AND newer.predecessor_task_id=predecessor
                AND newer.action='REQUESTED' AND newer.execution_revision>r.execution_revision)
                AND NOT EXISTS(SELECT 1 FROM app.execution_task_events h WHERE h.task_id=target AND h.action='EXECUTION_TASK_ASSIGNED' AND h.revision>r.execution_revision)
                );
                $body$;
                """);

            migrationBuilder.DropTable(
                name: "dependency_exception_revocations",
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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_action",
                schema: "app",
                table: "execution_task_events",
                sql: "action IN ('EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_event_task",
                schema: "app",
                table: "execution_task_events",
                sql: "(task_id IS NULL AND task_status IS NULL AND assigned_membership_id IS NULL AND action IN ('EXECUTION_STARTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED')) OR (task_id IS NOT NULL AND task_status IS NOT NULL AND task_status IN ('PENDING','IN_PROGRESS','BLOCKED','COMPLETED','SKIPPED') AND action IN ('EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','DEPENDENCY_EXCEPTION_REQUESTED','DEPENDENCY_EXCEPTION_APPROVED','DEPENDENCY_EXCEPTION_REJECTED'))");
        }
    }
}
