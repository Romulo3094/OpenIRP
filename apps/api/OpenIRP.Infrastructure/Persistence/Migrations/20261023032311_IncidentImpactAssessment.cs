using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentImpactAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<string>(
                name: "availability_impact",
                schema: "app",
                table: "incidents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confidentiality_impact",
                schema: "app",
                table: "incidents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "integrity_impact",
                schema: "app",
                table: "incidents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "availability_impact",
                schema: "app",
                table: "incident_events",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confidentiality_impact",
                schema: "app",
                table: "incident_events",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "integrity_impact",
                schema: "app",
                table: "incident_events",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_cia",
                schema: "app",
                table: "incidents",
                sql: "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_cia",
                schema: "app",
                table: "incident_events",
                sql: "(confidentiality_impact IS NULL OR confidentiality_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (integrity_impact IS NULL OR integrity_impact IN ('NONE','LOW','MEDIUM','HIGH')) AND (availability_impact IS NULL OR availability_impact IN ('NONE','LOW','MEDIUM','HIGH'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incidents WHERE confidentiality_impact IS NOT NULL OR integrity_impact IS NOT NULL OR availability_impact IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE confidentiality_impact IS NOT NULL OR integrity_impact IS NOT NULL OR availability_impact IS NOT NULL OR action = 'IMPACT_ASSESSED') THEN
                        RAISE EXCEPTION 'Cannot roll back CIA with recorded assessments; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_cia",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_cia",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "availability_impact",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "confidentiality_impact",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "integrity_impact",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "availability_impact",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "confidentiality_impact",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "integrity_impact",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
