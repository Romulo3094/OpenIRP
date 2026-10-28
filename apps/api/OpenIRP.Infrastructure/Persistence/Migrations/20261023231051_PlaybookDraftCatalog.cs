using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookDraftCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "playbooks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbooks", x => x.id);
                    table.CheckConstraint("ck_playbook_revision", "revision > 0");
                    table.CheckConstraint("ck_playbook_title", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "fk_playbooks_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbooks_incident_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "app",
                        principalTable: "incident_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbooks_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playbook_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playbook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    objective = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    instructions = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_versions", x => x.id);
                    table.CheckConstraint("ck_playbook_version_content", "btrim(objective) <> '' AND btrim(instructions) <> ''");
                    table.CheckConstraint("ck_playbook_version_draft", "version_number = 1 AND status = 'DRAFT'");
                    table.ForeignKey(
                        name: "fk_playbook_versions_playbooks_playbook_id",
                        column: x => x.playbook_id,
                        principalSchema: "app",
                        principalTable: "playbooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playbook_steps",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    requires_evidence = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_steps", x => x.id);
                    table.UniqueConstraint("ak_playbook_steps_version_id_id", x => new { x.version_id, x.id });
                    table.CheckConstraint("ck_playbook_step_code", "code ~ '^[A-Z][A-Z0-9_]{0,59}$'");
                    table.CheckConstraint("ck_playbook_step_content", "position BETWEEN 1 AND 10000 AND btrim(phase) <> '' AND btrim(instructions) <> ''");
                    table.ForeignKey(
                        name: "fk_playbook_steps_playbook_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playbook_capability_requirements",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_capability_requirements", x => x.id);
                    table.CheckConstraint("ck_playbook_requirement_level", "level IN ('REQUIRED','RECOMMENDED')");
                    table.ForeignKey(
                        name: "fk_playbook_capability_requirements_capability_catalog_capabil",
                        column: x => x.capability_id,
                        principalSchema: "app",
                        principalTable: "capability_catalog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_capability_requirements_playbook_steps_version_id_",
                        columns: x => new { x.version_id, x.step_id },
                        principalSchema: "app",
                        principalTable: "playbook_steps",
                        principalColumns: new[] { "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_capability_requirements_playbook_versions_version_",
                        column: x => x.version_id,
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playbook_step_dependencies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_step_dependencies", x => x.id);
                    table.CheckConstraint("ck_playbook_dependency_self", "predecessor_id <> successor_id");
                    table.ForeignKey(
                        name: "fk_playbook_step_dependencies_playbook_steps_version_id_predec",
                        columns: x => new { x.version_id, x.predecessor_id },
                        principalSchema: "app",
                        principalTable: "playbook_steps",
                        principalColumns: new[] { "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_step_dependencies_playbook_steps_version_id_succes",
                        columns: x => new { x.version_id, x.successor_id },
                        principalSchema: "app",
                        principalTable: "playbook_steps",
                        principalColumns: new[] { "version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_step_dependencies_playbook_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_capability_requirements_capability_id",
                schema: "app",
                table: "playbook_capability_requirements",
                column: "capability_id");

            migrationBuilder.CreateIndex(
                name: "ux_playbook_requirement_step",
                schema: "app",
                table: "playbook_capability_requirements",
                columns: new[] { "version_id", "step_id", "capability_id" },
                unique: true,
                filter: "step_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_playbook_requirement_version",
                schema: "app",
                table: "playbook_capability_requirements",
                columns: new[] { "version_id", "capability_id" },
                unique: true,
                filter: "step_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_step_dependencies_version_id_predecessor_id_succes",
                schema: "app",
                table: "playbook_step_dependencies",
                columns: new[] { "version_id", "predecessor_id", "successor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbook_step_dependencies_version_id_successor_id",
                schema: "app",
                table: "playbook_step_dependencies",
                columns: new[] { "version_id", "successor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_steps_version_id_code",
                schema: "app",
                table: "playbook_steps",
                columns: new[] { "version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbook_steps_version_id_position",
                schema: "app",
                table: "playbook_steps",
                columns: new[] { "version_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbook_versions_playbook_id_version_number",
                schema: "app",
                table: "playbook_versions",
                columns: new[] { "playbook_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbooks_category_id",
                schema: "app",
                table: "playbooks",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbooks_created_by_user_id",
                schema: "app",
                table: "playbooks",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbooks_organization_id_id",
                schema: "app",
                table: "playbooks",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_playbook_identity() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP IN ('DELETE','TRUNCATE') THEN
                        RAISE EXCEPTION 'Playbooks cannot be physically removed' USING ERRCODE='42501';
                    END IF;
                    IF NEW.id IS DISTINCT FROM OLD.id OR NEW.organization_id IS DISTINCT FROM OLD.organization_id
                       OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
                       OR NEW.created_at IS DISTINCT FROM OLD.created_at OR NEW.revision <= OLD.revision THEN
                        RAISE EXCEPTION 'Playbook identity is immutable and revision must advance' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_playbook_identity BEFORE UPDATE OR DELETE ON app.playbooks
                    FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_identity();
                CREATE TRIGGER protect_playbook_truncate BEFORE TRUNCATE ON app.playbooks
                    FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_identity();
                CREATE FUNCTION app.protect_playbook_version_identity() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP IN ('DELETE','TRUNCATE') THEN
                        RAISE EXCEPTION 'Playbook versions cannot be physically removed' USING ERRCODE='42501';
                    END IF;
                    IF NEW.id IS DISTINCT FROM OLD.id OR NEW.playbook_id IS DISTINCT FROM OLD.playbook_id
                       OR NEW.version_number IS DISTINCT FROM OLD.version_number THEN
                        RAISE EXCEPTION 'Playbook version identity is immutable' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_playbook_version_identity BEFORE UPDATE OR DELETE ON app.playbook_versions
                    FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_version_identity();
                CREATE TRIGGER protect_playbook_version_truncate BEFORE TRUNCATE ON app.playbook_versions
                    FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_version_identity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                    IF EXISTS (SELECT 1 FROM app.playbooks) THEN
                        RAISE EXCEPTION 'Cannot roll back playbook drafts while playbook data exists';
                    END IF;
                END; $guard$;
                """);

            migrationBuilder.DropTable(
                name: "playbook_capability_requirements",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbook_step_dependencies",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbook_steps",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbook_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playbooks",
                schema: "app");
            migrationBuilder.Sql("DROP FUNCTION app.protect_playbook_identity(); DROP FUNCTION app.protect_playbook_version_identity();");
        }
    }
}
