using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogsCapabilitiesAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criticality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.UniqueConstraint("ak_assets_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_asset_criticality", "criticality IN ('LOW','MEDIUM','HIGH')");
                    table.CheckConstraint("ck_asset_kind", "kind IN ('SERVER','WORKSTATION','CLOUDSERVICE','ACCOUNT','APPLICATION','NETWORKDEVICE','CRITICALSERVICE','OTHER')");
                    table.CheckConstraint("ck_asset_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_asset_revision", "revision > 0");
                    table.CheckConstraint("ck_asset_status", "status IN ('ACTIVE','RETIRED')");
                    table.ForeignKey(
                        name: "fk_assets_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "capability_catalog",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capability_catalog", x => x.id);
                    table.CheckConstraint("ck_capability_catalog_code", "code ~ '^[A-Z][A-Z0-9_]{1,59}$'");
                    table.CheckConstraint("ck_capability_catalog_name", "btrim(name) <> '' AND btrim(category) <> ''");
                    table.CheckConstraint("ck_capability_catalog_revision", "revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "incident_categories",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_categories", x => x.id);
                    table.CheckConstraint("ck_incident_categories_code", "code ~ '^[A-Z][A-Z0-9_]{1,59}$'");
                    table.CheckConstraint("ck_incident_categories_name", "btrim(name) <> '' AND btrim(category) <> ''");
                    table.CheckConstraint("ck_incident_categories_revision", "revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "organization_capabilities",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_capabilities", x => x.id);
                    table.UniqueConstraint("ak_organization_capabilities_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_org_capability_availability", "availability IN ('UNKNOWN','AVAILABLE','UNAVAILABLE')");
                    table.CheckConstraint("ck_org_capability_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_organization_capabilities_capability_catalog_capability_id",
                        column: x => x.capability_id,
                        principalSchema: "app",
                        principalTable: "capability_catalog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_capabilities_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "capability_catalog",
                columns: new[] { "id", "category", "code", "description", "is_active", "name", "revision" },
                values: new object[,]
                {
                    { new Guid("26000000-0000-4000-8000-000000000001"), "PERSONNEL", "IT_STAFF", null, true, "Personal de TI", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000002"), "PERSONNEL", "SECURITY_SPECIALIST", null, true, "Especialista de seguridad", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000003"), "PERSONNEL", "EXTERNAL_SUPPORT", null, true, "Soporte externo", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000004"), "PROTECTION", "ENDPOINT_PROTECTION", null, true, "Protección de endpoints", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000005"), "VISIBILITY", "SECURITY_LOGS", null, true, "Registros de seguridad", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000006"), "ACCESS", "MFA", null, true, "Autenticación multifactor", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000007"), "RECOVERY", "BACKUPS", null, true, "Respaldos", 1L },
                    { new Guid("26000000-0000-4000-8000-000000000008"), "RESPONSE", "DEVICE_ISOLATION", null, true, "Aislamiento de dispositivos", 1L }
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "incident_categories",
                columns: new[] { "id", "category", "code", "description", "is_active", "name", "revision" },
                values: new object[,]
                {
                    { new Guid("26000000-0000-4000-8100-000000000001"), "SOCIAL_ENGINEERING", "PHISHING", null, true, "Phishing", 1L },
                    { new Guid("26000000-0000-4000-8100-000000000002"), "IDENTITY", "ACCOUNT_COMPROMISE", null, true, "Compromiso de cuenta", 1L },
                    { new Guid("26000000-0000-4000-8100-000000000003"), "MALWARE", "RANSOMWARE", null, true, "Ransomware", 1L }
                });

            migrationBuilder.CreateIndex(
                name: "ix_capability_catalog_code",
                schema: "app",
                table: "capability_catalog",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incident_categories_code",
                schema: "app",
                table: "incident_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_capabilities_capability_id",
                schema: "app",
                table: "organization_capabilities",
                column: "capability_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_capabilities_organization_id_capability_id",
                schema: "app",
                table: "organization_capabilities",
                columns: new[] { "organization_id", "capability_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "incident_categories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organization_capabilities",
                schema: "app");

            migrationBuilder.DropTable(
                name: "capability_catalog",
                schema: "app");
        }
    }
}
