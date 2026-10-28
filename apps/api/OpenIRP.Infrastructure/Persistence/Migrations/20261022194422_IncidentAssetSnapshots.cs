using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncidentAssetSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddColumn<string>(
                name: "details_json",
                schema: "app",
                table: "incident_events",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "incident_assets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reported_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_context = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reported_criticality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reported_at_revision = table.Column<long>(type: "bigint", nullable: false),
                    linked_at_revision = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_assets", x => x.id);
                    table.CheckConstraint("ck_incident_asset_criticality", "reported_criticality IN ('LOW','MEDIUM','HIGH')");
                    table.CheckConstraint("ck_incident_asset_link", "(asset_id IS NULL AND linked_at_revision IS NULL) OR (asset_id IS NOT NULL AND linked_at_revision IS NOT NULL)");
                    table.CheckConstraint("ck_incident_asset_name", "btrim(reported_name) <> ''");
                    table.CheckConstraint("ck_incident_asset_revisions", "reported_at_revision > 1 AND (linked_at_revision IS NULL OR linked_at_revision >= reported_at_revision)");
                    table.CheckConstraint("ck_incident_asset_type", "reported_type IN ('SERVER','WORKSTATION','CLOUDSERVICE','ACCOUNT','APPLICATION','NETWORKDEVICE','CRITICALSERVICE','OTHER')");
                    table.ForeignKey(
                        name: "fk_incident_assets_assets_organization_id_asset_id",
                        columns: x => new { x.organization_id, x.asset_id },
                        principalSchema: "app",
                        principalTable: "assets",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_assets_incidents_organization_id_incident_id",
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
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED','ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_details",
                schema: "app",
                table: "incident_events",
                sql: "jsonb_typeof(details_json) = 'object' AND octet_length(details_json::text) <= 32768");

            migrationBuilder.CreateIndex(
                name: "ix_incident_assets_organization_id_asset_id",
                schema: "app",
                table: "incident_assets",
                columns: new[] { "organization_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_assets_organization_id_incident_id_asset_id",
                schema: "app",
                table: "incident_assets",
                columns: new[] { "organization_id", "incident_id", "asset_id" },
                unique: true,
                filter: "asset_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_incident_assets_organization_id_incident_id_id",
                schema: "app",
                table: "incident_assets",
                columns: new[] { "organization_id", "incident_id", "id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_incident_asset() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $body$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Incident asset reports cannot be removed' USING ERRCODE = '42501';
                    END IF;
                    IF ROW(NEW.id, NEW.organization_id, NEW.incident_id, NEW.reported_name, NEW.reported_type,
                           NEW.reported_context, NEW.reported_criticality, NEW.reported_at_revision, NEW.created_at)
                        IS DISTINCT FROM
                       ROW(OLD.id, OLD.organization_id, OLD.incident_id, OLD.reported_name, OLD.reported_type,
                           OLD.reported_context, OLD.reported_criticality, OLD.reported_at_revision, OLD.created_at)
                       OR OLD.asset_id IS NOT NULL OR NEW.asset_id IS NULL OR NEW.linked_at_revision IS NULL THEN
                        RAISE EXCEPTION 'Only first inventory linkage may change an incident asset report' USING ERRCODE = '42501';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER incident_assets_protect_snapshot BEFORE UPDATE ON app.incident_assets
                    FOR EACH ROW EXECUTE FUNCTION app.protect_incident_asset();
                CREATE TRIGGER incident_assets_no_removal BEFORE DELETE OR TRUNCATE ON app.incident_assets
                    FOR EACH STATEMENT EXECUTE FUNCTION app.protect_incident_asset();
                REVOKE ALL ON FUNCTION app.protect_incident_asset() FROM PUBLIC;
                REVOKE ALL ON TABLE app.incident_assets FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.incident_assets)
                       OR EXISTS (SELECT 1 FROM app.incident_events WHERE action IN ('ASSET_REPORTED','ASSET_ATTACHED','ASSET_LINKED')) THEN
                        RAISE EXCEPTION 'Cannot roll back incident assets with recorded history; preserve data and plan recovery';
                    END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "incident_assets",
                schema: "app");
            migrationBuilder.Sql("DROP FUNCTION app.protect_incident_asset();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incident_event_details",
                schema: "app",
                table: "incident_events");

            migrationBuilder.DropColumn(
                name: "details_json",
                schema: "app",
                table: "incident_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incident_event_action",
                schema: "app",
                table: "incident_events",
                sql: "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
        }
    }
}
