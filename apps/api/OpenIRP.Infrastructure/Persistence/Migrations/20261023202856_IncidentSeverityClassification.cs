using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentSeverityClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<string>(
                name: "maximum_severity_reached",
                schema: "app",
                table: "incidents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity",
                schema: "app",
                table: "incidents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "severity_assessed_at_revision",
                schema: "app",
                table: "incidents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity_rule_version",
                schema: "app",
                table: "incidents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maximum_severity_reached",
                schema: "app",
                table: "incident_events",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity",
                schema: "app",
                table: "incident_events",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "severity_assessed_at_revision",
                schema: "app",
                table: "incident_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity_rule_version",
                schema: "app",
                table: "incident_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_severity",
                schema: "app",
                table: "incidents",
                sql: "(severity IS NULL OR severity IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (maximum_severity_reached IS NULL OR maximum_severity_reached IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (severity IS NULL OR (maximum_severity_reached IS NOT NULL AND (CASE maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END) >= (CASE severity WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END))) AND ((severity_rule_version IS NULL AND severity IS NULL AND maximum_severity_reached IS NULL AND severity_assessed_at_revision IS NULL) OR (severity_rule_version IS NOT NULL AND severity_rule_version = 'openirp-severity-v1' AND severity_assessed_at_revision IS NOT NULL AND severity_assessed_at_revision > 0 AND severity_assessed_at_revision <= revision))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED','SEVERITY_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_severity",
                schema: "app",
                table: "incident_events",
                sql: "(severity IS NULL OR severity IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (maximum_severity_reached IS NULL OR maximum_severity_reached IN ('LOW','MEDIUM','HIGH','CRITICAL')) AND (severity IS NULL OR (maximum_severity_reached IS NOT NULL AND (CASE maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END) >= (CASE severity WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 END))) AND ((severity_rule_version IS NULL AND severity IS NULL AND maximum_severity_reached IS NULL AND severity_assessed_at_revision IS NULL) OR (severity_rule_version IS NOT NULL AND severity_rule_version = 'openirp-severity-v1' AND severity_assessed_at_revision IS NOT NULL AND severity_assessed_at_revision > 0 AND severity_assessed_at_revision <= revision))");
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_incident_severity_maximum() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF OLD.maximum_severity_reached IS NOT NULL AND
                        (NEW.maximum_severity_reached IS NULL OR
                        (CASE NEW.maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 ELSE 0 END)
                        < (CASE OLD.maximum_severity_reached WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 WHEN 'CRITICAL' THEN 4 ELSE 0 END)) THEN
                        RAISE EXCEPTION 'Incident historical maximum cannot decrease' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_incident_severity_maximum BEFORE UPDATE ON app.incidents
                    FOR EACH ROW EXECUTE FUNCTION app.protect_incident_severity_maximum();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incidents WHERE severity_rule_version IS NOT NULL OR severity IS NOT NULL OR maximum_severity_reached IS NOT NULL OR severity_assessed_at_revision IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE severity_rule_version IS NOT NULL OR severity IS NOT NULL OR maximum_severity_reached IS NOT NULL OR severity_assessed_at_revision IS NOT NULL OR action = 'SEVERITY_ASSESSED') THEN
                        RAISE EXCEPTION 'Cannot roll back incident severity with recorded classifications; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                DROP TRIGGER protect_incident_severity_maximum ON app.incidents;
                DROP FUNCTION app.protect_incident_severity_maximum();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_severity",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_severity",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "maximum_severity_reached",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "severity",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "severity_assessed_at_revision",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "severity_rule_version",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "maximum_severity_reached",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "severity",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "severity_assessed_at_revision",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "severity_rule_version",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
