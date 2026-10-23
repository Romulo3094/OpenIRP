using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentCapabilityAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.CreateTable(
                name: "incident_capability_assessments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    limitations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    assessed_at_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_capability_assessments", x => x.id);
                    table.CheckConstraint("ck_incident_capability_availability", "availability IN ('UNKNOWN','AVAILABLE','UNAVAILABLE')");
                    table.CheckConstraint("ck_incident_capability_limitations", "limitations IS NULL OR btrim(limitations) <> ''");
                    table.CheckConstraint("ck_incident_capability_reason", "btrim(reason) <> ''");
                    table.CheckConstraint("ck_incident_capability_revision", "assessed_at_revision > 1");
                    table.ForeignKey(
                        name: "fk_incident_capability_assessments_capability_catalog_capabili",
                        column: x => x.capability_id,
                        principalSchema: "app",
                        principalTable: "capability_catalog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_capability_assessments_incidents_organization_id_i",
                        columns: x => new { x.organization_id, x.incident_id },
                        principalSchema: "app",
                        principalTable: "incidents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED','CAPABILITY_ASSESSED','CAPABILITY_INHERITED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.CreateIndex(
                name: "ix_incident_capability_assessments_capability_id",
                schema: "app",
                table: "incident_capability_assessments",
                column: "capability_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_capability_assessments_organization_id_incident_id",
                schema: "app",
                table: "incident_capability_assessments",
                columns: new[] { "organization_id", "incident_id", "capability_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_incident_capability_assessment() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $body$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Incident capability decisions cannot be removed; inherit instead' USING ERRCODE = '42501';
                    END IF;
                    IF ROW(NEW.id, NEW.organization_id, NEW.incident_id, NEW.capability_id)
                        IS DISTINCT FROM ROW(OLD.id, OLD.organization_id, OLD.incident_id, OLD.capability_id)
                       OR NEW.assessed_at_revision <= OLD.assessed_at_revision THEN
                        RAISE EXCEPTION 'Assessment identity is immutable and its revision must advance' USING ERRCODE = '42501';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER incident_capabilities_protect_identity BEFORE UPDATE ON app.incident_capability_assessments
                    FOR EACH ROW EXECUTE FUNCTION app.protect_incident_capability_assessment();
                CREATE TRIGGER incident_capabilities_no_removal BEFORE DELETE OR TRUNCATE ON app.incident_capability_assessments
                    FOR EACH STATEMENT EXECUTE FUNCTION app.protect_incident_capability_assessment();
                REVOKE ALL ON FUNCTION app.protect_incident_capability_assessment() FROM PUBLIC;
                REVOKE ALL ON TABLE app.incident_capability_assessments FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incident_capability_assessments)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE action IN ('CAPABILITY_ASSESSED','CAPABILITY_INHERITED')) THEN
                        RAISE EXCEPTION 'Cannot roll back incident capabilities with recorded history; preserve data and plan recovery';
                    END IF;
                END; $guard$;
                """);
            migrationBuilder.DropTable(
                name: "incident_capability_assessments",
                schema: "app");
            migrationBuilder.Sql("DROP FUNCTION app.protect_incident_capability_assessment();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
