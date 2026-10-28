using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentCriticalityAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<string>(
                name: "assessed_criticality",
                schema: "app",
                table: "incidents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "criticality_assessed_at_revision",
                schema: "app",
                table: "incidents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assessed_criticality",
                schema: "app",
                table: "incident_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "criticality_assessed_at_revision",
                schema: "app",
                table: "incident_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_criticality",
                schema: "app",
                table: "incidents",
                sql: "(assessed_criticality IS NULL OR assessed_criticality IN ('NOTAPPLICABLE','LOW','MEDIUM','HIGH')) AND (criticality_assessed_at_revision IS NULL OR (criticality_assessed_at_revision > 1 AND criticality_assessed_at_revision <= revision)) AND (assessed_criticality IS NULL OR criticality_assessed_at_revision IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED','CRITICALITY_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_criticality",
                schema: "app",
                table: "incident_events",
                sql: "(assessed_criticality IS NULL OR assessed_criticality IN ('NOTAPPLICABLE','LOW','MEDIUM','HIGH')) AND (criticality_assessed_at_revision IS NULL OR (criticality_assessed_at_revision > 1 AND criticality_assessed_at_revision <= revision)) AND (assessed_criticality IS NULL OR criticality_assessed_at_revision IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incidents WHERE assessed_criticality IS NOT NULL OR criticality_assessed_at_revision IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE assessed_criticality IS NOT NULL OR criticality_assessed_at_revision IS NOT NULL OR action = 'CRITICALITY_ASSESSED') THEN
                        RAISE EXCEPTION 'Cannot roll back incident criticality with recorded assessments; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_criticality",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_criticality",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "assessed_criticality",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "criticality_assessed_at_revision",
                schema: "app",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "assessed_criticality",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "criticality_assessed_at_revision",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED','IMPACT_ASSESSED','CONTEXT_ASSESSED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
