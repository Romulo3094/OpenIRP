using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentLifecycleClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_status",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_status",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<Guid>(
                name: "closure_decided_by_user_id",
                schema: "app",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "closure_decision_revision",
                schema: "app",
                table: "incidents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_request_id",
                schema: "app",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "closure_request_revision",
                schema: "app",
                table: "incidents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_requested_by_user_id",
                schema: "app",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closure_review_status",
                schema: "app",
                table: "incidents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "closure_self_approval",
                schema: "app",
                table: "incidents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closure_self_approval_justification",
                schema: "app",
                table: "incidents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution",
                schema: "app",
                table: "incidents",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_decided_by_user_id",
                schema: "app",
                table: "incident_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "closure_decision_revision",
                schema: "app",
                table: "incident_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_request_id",
                schema: "app",
                table: "incident_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "closure_request_revision",
                schema: "app",
                table: "incident_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_requested_by_user_id",
                schema: "app",
                table: "incident_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closure_review_status",
                schema: "app",
                table: "incident_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "closure_self_approval",
                schema: "app",
                table: "incident_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "closure_self_approval_justification",
                schema: "app",
                table: "incident_events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution",
                schema: "app",
                table: "incident_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidents_closure_decided_by_user_id",
                schema: "app",
                table: "incidents",
                column: "closure_decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_closure_request_id",
                schema: "app",
                table: "incidents",
                column: "closure_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidents_closure_requested_by_user_id",
                schema: "app",
                table: "incidents",
                column: "closure_requested_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_closure",
                schema: "app",
                table: "incidents",
                sql: "((closure_request_id IS NULL AND closure_request_revision IS NULL AND closure_requested_by_user_id IS NULL AND closure_review_status IS NULL AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_request_id IS NOT NULL AND closure_request_revision IS NOT NULL AND closure_request_revision > 1 AND closure_request_revision <= revision AND closure_requested_by_user_id IS NOT NULL AND closure_review_status IS NOT NULL AND closure_review_status IN ('PENDING','APPROVED','REJECTED','INVALIDATED') AND ((closure_review_status = 'PENDING' AND status = 'RESOLVED' AND closure_request_revision = revision AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_review_status <> 'PENDING' AND closure_decided_by_user_id IS NOT NULL AND closure_decision_revision IS NOT NULL AND closure_decision_revision > closure_request_revision AND closure_decision_revision <= revision AND closure_self_approval IS NOT NULL AND (closure_review_status <> 'APPROVED' OR status = 'CLOSED') AND ((closure_self_approval = TRUE AND closure_review_status = 'APPROVED' AND closure_decided_by_user_id = closure_requested_by_user_id AND closure_self_approval_justification IS NOT NULL AND btrim(closure_self_approval_justification) <> '') OR (closure_self_approval = FALSE AND closure_self_approval_justification IS NULL AND (closure_review_status <> 'APPROVED' OR closure_decided_by_user_id <> closure_requested_by_user_id)))))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_lifecycle",
                schema: "app",
                table: "incidents",
                sql: "(resolution IS NULL OR btrim(resolution) <> '') AND (status NOT IN ('RESOLVED','CLOSED') OR resolution IS NOT NULL) AND (status <> 'CLOSED' OR (severity IS NOT NULL AND maximum_severity_reached IS NOT NULL AND severity_rule_version IS NOT NULL AND assigned_membership_id IS NOT NULL AND (maximum_severity_reached IN ('LOW','MEDIUM') OR (maximum_severity_reached IN ('HIGH','CRITICAL') AND closure_review_status IS NOT NULL AND closure_review_status = 'APPROVED' AND closure_decision_revision = revision))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_status",
                schema: "app",
                table: "incidents",
                sql: "status IN ('REGISTERED','UNDER_EVALUATION','IN_RESPONSE','RESOLVED','CLOSED','DISCARDED')");

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_closure_decided_by_user_id",
                schema: "app",
                table: "incident_events",
                column: "closure_decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_closure_requested_by_user_id",
                schema: "app",
                table: "incident_events",
                column: "closure_requested_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED','STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_closure",
                schema: "app",
                table: "incident_events",
                sql: "((closure_request_id IS NULL AND closure_request_revision IS NULL AND closure_requested_by_user_id IS NULL AND closure_review_status IS NULL AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_request_id IS NOT NULL AND closure_request_revision IS NOT NULL AND closure_request_revision > 1 AND closure_request_revision <= revision AND closure_requested_by_user_id IS NOT NULL AND closure_review_status IS NOT NULL AND closure_review_status IN ('PENDING','APPROVED','REJECTED','INVALIDATED') AND ((closure_review_status = 'PENDING' AND status = 'RESOLVED' AND closure_request_revision = revision AND closure_decided_by_user_id IS NULL AND closure_decision_revision IS NULL AND closure_self_approval IS NULL AND closure_self_approval_justification IS NULL) OR (closure_review_status <> 'PENDING' AND closure_decided_by_user_id IS NOT NULL AND closure_decision_revision IS NOT NULL AND closure_decision_revision > closure_request_revision AND closure_decision_revision <= revision AND closure_self_approval IS NOT NULL AND (closure_review_status <> 'APPROVED' OR status = 'CLOSED') AND ((closure_self_approval = TRUE AND closure_review_status = 'APPROVED' AND closure_decided_by_user_id = closure_requested_by_user_id AND closure_self_approval_justification IS NOT NULL AND btrim(closure_self_approval_justification) <> '') OR (closure_self_approval = FALSE AND closure_self_approval_justification IS NULL AND (closure_review_status <> 'APPROVED' OR closure_decided_by_user_id <> closure_requested_by_user_id)))))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_lifecycle",
                schema: "app",
                table: "incident_events",
                sql: "(resolution IS NULL OR btrim(resolution) <> '') AND (status NOT IN ('RESOLVED','CLOSED') OR resolution IS NOT NULL) AND (status <> 'CLOSED' OR (severity IS NOT NULL AND maximum_severity_reached IS NOT NULL AND severity_rule_version IS NOT NULL AND assigned_membership_id IS NOT NULL AND (maximum_severity_reached IN ('LOW','MEDIUM') OR (maximum_severity_reached IN ('HIGH','CRITICAL') AND closure_review_status IS NOT NULL AND closure_review_status = 'APPROVED' AND closure_decision_revision = revision))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_status",
                schema: "app",
                table: "incident_events",
                sql: "status IN ('REGISTERED','UNDER_EVALUATION','IN_RESPONSE','RESOLVED','CLOSED','DISCARDED')");

            migrationBuilder.AddForeignKey(
                name: "fk_incident_events_asp_net_users_closure_decided_by_user_id",
                schema: "app",
                table: "incident_events",
                column: "closure_decided_by_user_id",
                principalSchema: "auth",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_incident_events_asp_net_users_closure_requested_by_user_id",
                schema: "app",
                table: "incident_events",
                column: "closure_requested_by_user_id",
                principalSchema: "auth",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_incidents_asp_net_users_closure_decided_by_user_id",
                schema: "app",
                table: "incidents",
                column: "closure_decided_by_user_id",
                principalSchema: "auth",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_incidents_asp_net_users_closure_requested_by_user_id",
                schema: "app",
                table: "incidents",
                column: "closure_requested_by_user_id",
                principalSchema: "auth",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_incident_lifecycle() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF OLD.status IN ('CLOSED','DISCARDED') THEN
                        IF NEW.status <> 'UNDER_EVALUATION' OR NEW.resolution IS NOT NULL OR NEW.revision <= OLD.revision THEN
                            RAISE EXCEPTION 'Terminal incident must be reopened' USING ERRCODE='23514';
                        END IF;
                    ELSIF NEW.status <> OLD.status AND NOT (
                        (OLD.status='REGISTERED' AND NEW.status='UNDER_EVALUATION') OR
                        (OLD.status='UNDER_EVALUATION' AND NEW.status IN ('IN_RESPONSE','DISCARDED')) OR
                        (OLD.status='IN_RESPONSE' AND NEW.status IN ('UNDER_EVALUATION','RESOLVED')) OR
                        (OLD.status='RESOLVED' AND NEW.status IN ('IN_RESPONSE','CLOSED'))) THEN
                        RAISE EXCEPTION 'Invalid incident state transition' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_incident_lifecycle BEFORE UPDATE ON app.incidents
                    FOR EACH ROW EXECUTE FUNCTION app.protect_incident_lifecycle();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incidents WHERE status <> 'REGISTERED' OR resolution IS NOT NULL OR closure_request_id IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE status <> 'REGISTERED' OR resolution IS NOT NULL OR closure_request_id IS NOT NULL
                            OR action IN ('STATE_CHANGED','CLOSURE_REQUESTED','CLOSURE_APPROVED','CLOSURE_REJECTED')) THEN
                        RAISE EXCEPTION 'Cannot roll back incident lifecycle with recorded workflow or closure history; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                DROP TRIGGER protect_incident_lifecycle ON app.incidents;
                DROP FUNCTION app.protect_incident_lifecycle();
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_incident_events_asp_net_users_closure_decided_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropForeignKey(
                name: "fk_incident_events_asp_net_users_closure_requested_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropForeignKey(
                name: "fk_incidents_asp_net_users_closure_decided_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_incidents_asp_net_users_closure_requested_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "ix_incidents_closure_decided_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "ix_incidents_closure_request_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "ix_incidents_closure_requested_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_closure",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_lifecycle",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_status",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "ix_incident_events_closure_decided_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropIndex(
                name: "ix_incident_events_closure_requested_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_closure",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_lifecycle",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_status",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_decided_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_decision_revision",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_request_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_request_revision",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_requested_by_user_id",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_review_status",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_self_approval",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_self_approval_justification",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "resolution",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "closure_decided_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_decision_revision",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_request_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_request_revision",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_requested_by_user_id",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_review_status",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_self_approval",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "closure_self_approval_justification",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "resolution",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_status",
                schema: "app",
                table: "incidents",
                sql: "status = 'REGISTERED'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_status",
                schema: "app",
                table: "incident_events",
                sql: "status = 'REGISTERED'");
        }
    }
}
