using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DependencyExceptions : Migration
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
                name: "dependency_exceptions",
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
                    table.PrimaryKey("pk_dependency_exceptions", x => x.id);
                    table.UniqueConstraint("ak_dependency_exception_scope", x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.dependency_id, x.predecessor_task_id, x.skip_authorization_id, x.assigned_membership_id, x.id });
                    table.CheckConstraint("ck_dependency_exception_action", "(action='REQUESTED' AND request_id IS NULL) OR (action IN ('APPROVED','REJECTED') AND request_id IS NOT NULL AND request_id<>id)");
                    table.CheckConstraint("ck_dependency_exception_self", "(NOT self_approval AND self_approval_justification IS NULL) OR (self_approval AND action='APPROVED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification)<>'')");
                    table.CheckConstraint("ck_dependency_exception_values", "execution_revision>1 AND incident_revision>1 AND btrim(reason)<>'' AND btrim(impact)<>'' AND btrim(residual_risk)<>'' AND jsonb_typeof(dependency_context_json)='object' AND octet_length(dependency_context_json::text)<=262144");
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_dependency_exceptions_organization_id",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id, x.dependency_id, x.predecessor_task_id, x.skip_authorization_id, x.assigned_membership_id, x.request_id },
                        principalSchema: "app",
                        principalTable: "dependency_exceptions",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "dependency_id", "predecessor_task_id", "skip_authorization_id", "assigned_membership_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_execution_tasks_organization_id_incid",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.predecessor_task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_execution_tasks_organization_id_incid1",
                        columns: x => new { x.organization_id, x.incident_id, x.version_id, x.execution_id, x.task_id },
                        principalSchema: "app",
                        principalTable: "execution_tasks",
                        principalColumns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_organization_memberships_organization",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_playbook_step_dependencies_dependency",
                        column: x => x.dependency_id,
                        principalSchema: "app",
                        principalTable: "playbook_step_dependencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dependency_exceptions_skip_authorizations_skip_authorizatio",
                        column: x => x.skip_authorization_id,
                        principalSchema: "app",
                        principalTable: "skip_authorizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_actor_user_id",
                schema: "app",
                table: "dependency_exceptions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_dependency_id",
                schema: "app",
                table: "dependency_exceptions",
                column: "dependency_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_organization_id_assigned_membership_id",
                schema: "app",
                table: "dependency_exceptions",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_organization_id_incident_id_version_i",
                schema: "app",
                table: "dependency_exceptions",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "predecessor_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_organization_id_incident_id_version_i1",
                schema: "app",
                table: "dependency_exceptions",
                columns: new[] { "organization_id", "incident_id", "version_id", "execution_id", "task_id", "dependency_id", "predecessor_task_id", "skip_authorization_id", "assigned_membership_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_request_id",
                schema: "app",
                table: "dependency_exceptions",
                column: "request_id",
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_skip_authorization_id",
                schema: "app",
                table: "dependency_exceptions",
                column: "skip_authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_dependency_exceptions_task_id_predecessor_task_id_execution",
                schema: "app",
                table: "dependency_exceptions",
                columns: new[] { "task_id", "predecessor_task_id", "execution_revision" },
                unique: true);
            migrationBuilder.Sql("""

                CREATE FUNCTION app.dependency_exception_context(run uuid, target uuid) RETURNS jsonb LANGUAGE sql STABLE AS $body$
                 SELECT jsonb_build_object(
                  'format','dependency-exception-context-v1',
                  'incidentFingerprint',encode(sha256(convert_to((to_jsonb(i)-'revision'-'created_at')::text,'UTF8')),'hex'),
                  'riskRevision',COALESCE((SELECT max(h.revision) FROM app.incident_events h WHERE h.organization_id=i.organization_id AND h.incident_id=i.id
                    AND h.action NOT LIKE 'EXECUTION_%' AND h.action NOT LIKE 'STEP_APPROVAL_%' AND h.action NOT LIKE 'SKIP_AUTHORIZATION_%'
                    AND h.action NOT LIKE 'DEPENDENCY_EXCEPTION_%' AND h.action<>'EVIDENCE_REGISTERED'),0),
                  'capabilities',COALESCE((SELECT jsonb_agg(jsonb_build_object(
                     'capabilityId',c.id,'catalogActive',c.is_active,
                     'organizationAvailability',initcap(COALESCE(p.availability,'UNKNOWN')),
                     'overrideAvailability',CASE WHEN a.availability IS NULL THEN NULL ELSE initcap(a.availability) END,
                     'effectiveAvailability',initcap(CASE WHEN NOT c.is_active THEN 'UNKNOWN' ELSE COALESCE(a.availability,p.availability,'UNKNOWN') END),
                     'source',CASE WHEN NOT c.is_active THEN 'InactiveCatalog' WHEN a.availability IS NOT NULL THEN 'IncidentAssessment' WHEN p.id IS NOT NULL THEN 'OrganizationProfile' ELSE 'Undeclared' END,
                     'catalogRevision',c.revision,'catalogFingerprint',encode(sha256(convert_to(to_jsonb(c)::text,'UTF8')),'hex'),
                     'profileRevision',COALESCE(p.revision,0),'profileFingerprint',encode(sha256(convert_to(to_jsonb(p)::text,'UTF8')),'hex'),
                     'assessmentId',a.id,'assessedAtRevision',a.assessed_at_revision,'assessmentFingerprint',encode(sha256(convert_to(to_jsonb(a)::text,'UTF8')),'hex')) ORDER BY c.id)
                    FROM app.capability_catalog c LEFT JOIN app.organization_capabilities p ON p.capability_id=c.id AND p.organization_id=i.organization_id
                    LEFT JOIN app.incident_capability_assessments a ON a.capability_id=c.id AND a.organization_id=i.organization_id AND a.incident_id=i.id
                    WHERE EXISTS(SELECT 1 FROM app.playbook_capability_requirements r WHERE r.version_id=e.version_id AND r.capability_id=c.id
                      AND (r.step_id IS NULL OR r.step_id=t.step_id))),'[]'::jsonb))
                 FROM app.playbook_executions e JOIN app.incidents i ON i.id=e.incident_id AND i.organization_id=e.organization_id
                 JOIN app.execution_tasks t ON t.execution_id=e.id AND t.organization_id=e.organization_id AND t.incident_id=e.incident_id AND t.version_id=e.version_id
                 WHERE e.id=run AND t.id=target;
                $body$;

                CREATE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid, snapshot jsonb) RETURNS boolean LANGUAGE sql STABLE AS $body$
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
                CREATE FUNCTION app.has_dependency_exception(target uuid, predecessor uuid, assigned uuid) RETURNS boolean LANGUAGE sql STABLE AS $body$
                 SELECT app.has_dependency_exception(target,predecessor,assigned,app.dependency_exception_context(t.execution_id,t.id))
                 FROM app.execution_tasks t WHERE t.id=target;
                $body$;

                CREATE FUNCTION app.execution_dependencies_satisfied(run uuid, target uuid, assigned uuid) RETURNS boolean LANGUAGE sql STABLE AS $body$
                 WITH snapshot AS MATERIALIZED(SELECT app.dependency_exception_context(run,target) AS value)
                 SELECT EXISTS(SELECT 1 FROM app.execution_tasks WHERE id=target AND execution_id=run)
                 AND NOT EXISTS(
                  SELECT 1 FROM app.execution_tasks t JOIN app.playbook_step_dependencies d ON d.version_id=t.version_id AND d.successor_id=t.step_id
                  JOIN app.execution_tasks p ON p.execution_id=t.execution_id AND p.step_id=d.predecessor_id CROSS JOIN snapshot
                  WHERE t.id=target AND t.execution_id=run AND p.status<>'COMPLETED'
                   AND NOT (p.status='SKIPPED' AND app.has_dependency_exception(target,p.id,assigned,snapshot.value)));
                $body$;

                CREATE FUNCTION app.protect_dependency_exception() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Dependency exceptions are append-only' USING ERRCODE='42501'; END IF;
                 IF NEW.action='REQUESTED' THEN
                  IF app.has_dependency_exception(NEW.task_id,NEW.predecessor_task_id,NEW.assigned_membership_id)
                   OR EXISTS(SELECT 1 FROM app.dependency_exceptions r WHERE r.task_id=NEW.task_id AND r.predecessor_task_id=NEW.predecessor_task_id AND r.action='REQUESTED'
                    AND r.execution_revision=NEW.execution_revision-1 AND r.incident_revision=NEW.incident_revision-1 AND r.assigned_membership_id=NEW.assigned_membership_id
                    AND r.dependency_context_json=NEW.dependency_context_json
                    AND NOT EXISTS(SELECT 1 FROM app.dependency_exceptions d WHERE d.request_id=r.id AND d.action='REJECTED')) THEN
                   RAISE EXCEPTION 'A current authorization/request already exists' USING ERRCODE='23514';
                  END IF;
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_dependency_exception BEFORE INSERT OR UPDATE OR DELETE ON app.dependency_exceptions FOR EACH ROW EXECUTE FUNCTION app.protect_dependency_exception();
                CREATE TRIGGER protect_dependency_exception_truncate BEFORE TRUNCATE ON app.dependency_exceptions FOR EACH STATEMENT EXECUTE FUNCTION app.protect_dependency_exception();

                CREATE FUNCTION app.check_dependency_exception() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_dependency_exception AFTER INSERT ON app.dependency_exceptions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_dependency_exception();

                CREATE FUNCTION app.check_task_dependency_exceptions() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE task app.execution_tasks%ROWTYPE;
                BEGIN
                 SELECT * INTO task FROM app.execution_tasks WHERE id=NEW.id;
                 IF task.status IN ('IN_PROGRESS','COMPLETED') AND NOT app.execution_dependencies_satisfied(task.execution_id,task.id,task.assigned_membership_id) THEN
                  RAISE EXCEPTION 'Final task state requires completed dependencies or current edge exceptions' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_task_dependency_exceptions AFTER INSERT OR UPDATE ON app.execution_tasks DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_task_dependency_exceptions();

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
                                                                                END; $body$;
                CREATE OR REPLACE FUNCTION app.protect_step_approval() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                                END; $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.dependency_exceptions) OR EXISTS(SELECT 1 FROM app.incident_events WHERE action LIKE 'DEPENDENCY_EXCEPTION_%') THEN
                  RAISE EXCEPTION 'Cannot roll back dependency exceptions while data or history exists'; END IF;
                END; $guard$;

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
                CREATE OR REPLACE FUNCTION app.protect_step_approval() RETURNS trigger LANGUAGE plpgsql AS $body$
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

                DROP TRIGGER check_task_dependency_exceptions ON app.execution_tasks;
                DROP FUNCTION app.check_task_dependency_exceptions();
                DROP TRIGGER check_dependency_exception ON app.dependency_exceptions;
                DROP FUNCTION app.check_dependency_exception();
                DROP TRIGGER protect_dependency_exception ON app.dependency_exceptions;
                DROP TRIGGER protect_dependency_exception_truncate ON app.dependency_exceptions;
                DROP FUNCTION app.protect_dependency_exception();
                DROP FUNCTION app.execution_dependencies_satisfied(uuid,uuid,uuid);
                DROP FUNCTION app.has_dependency_exception(uuid,uuid,uuid);
                DROP FUNCTION app.has_dependency_exception(uuid,uuid,uuid,jsonb);
                DROP FUNCTION app.dependency_exception_context(uuid,uuid);
                """);
            migrationBuilder.DropTable(
                name: "dependency_exceptions",
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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED','EVIDENCE_REGISTERED','EXECUTION_STARTED','EXECUTION_TASK_ASSIGNED','EXECUTION_TASK_CHANGED','STEP_APPROVAL_REQUESTED','STEP_APPROVAL_APPROVED','STEP_APPROVAL_REJECTED','SKIP_AUTHORIZATION_REQUESTED','SKIP_AUTHORIZATION_APPROVED','SKIP_AUTHORIZATION_REJECTED','EXECUTION_COMPLETED','EXECUTION_CANCELLED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

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
        }
    }
}
