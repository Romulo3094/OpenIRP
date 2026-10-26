using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentContextAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<string>(
                name: "operational_impact",
                schema: "app",
                table: "incidents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "app",
                table: "incidents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operational_impact",
                schema: "app",
                table: "incident_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "app",
                table: "incident_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_context",
                schema: "app",
                table: "incidents",
                sql: "(scope IS NULL OR scope IN ('SINGLE','MULTIPLE','ORGANIZATIONWIDE')) AND (operational_impact IS NULL OR operational_impact IN ('NONE','DEGRADED','MAJORINTERRUPTION','CRITICALINTERRUPTION'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_context",
                schema: "app",
                table: "incident_events",
                sql: "(scope IS NULL OR scope IN ('SINGLE','MULTIPLE','ORGANIZATIONWIDE')) AND (operational_impact IS NULL OR operational_impact IN ('NONE','DEGRADED','MAJORINTERRUPTION','CRITICALINTERRUPTION'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incidents WHERE scope IS NOT NULL OR operational_impact IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE scope IS NOT NULL OR operational_impact IS NOT NULL OR action = 'CONTEXT_ASSESSED') THEN
                        RAISE EXCEPTION 'Cannot roll back incident context with recorded assessments; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_context",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_context",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "operational_impact",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "operational_impact",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
