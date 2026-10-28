using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_version_draft",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.AddColumn<Guid>(
                name: "current_published_version_id",
                schema: "app",
                table: "playbooks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "app",
                table: "playbook_versions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "app",
                table: "playbook_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "review_request_id",
                schema: "app",
                table: "playbook_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_version_id",
                schema: "app",
                table: "playbook_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by_user_id",
                schema: "app",
                table: "playbook_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                schema: "app",
                table: "playbook_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE app.playbook_versions v SET title=p.title, category_id=p.category_id FROM app.playbooks p WHERE p.id=v.playbook_id;
                ALTER TABLE app.playbook_versions ALTER COLUMN title DROP DEFAULT, ALTER COLUMN category_id DROP DEFAULT;
                """);
            migrationBuilder.AddUniqueConstraint(
                name: "ak_playbook_versions_playbook_id_id",
                schema: "app",
                table: "playbook_versions",
                columns: new[] { "playbook_id", "id" });

            migrationBuilder.CreateTable(
                name: "playbook_reviews",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playbook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    self_approval = table.Column<bool>(type: "boolean", nullable: false),
                    self_approval_justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playbook_reviews", x => x.id);
                    table.CheckConstraint("ck_playbook_review_action", "action IN ('SUBMITTED','PUBLISHED','REJECTED','WITHDRAWN')");
                    table.CheckConstraint("ck_playbook_review_fields", "revision > 0 AND btrim(reason) <> '' AND content_hash ~ '^[A-F0-9]{64}$' AND ((action = 'WITHDRAWN' AND request_id IS NULL) OR (action <> 'WITHDRAWN' AND request_id IS NOT NULL))");
                    table.CheckConstraint("ck_playbook_review_self", "(self_approval AND action = 'PUBLISHED' AND self_approval_justification IS NOT NULL AND btrim(self_approval_justification) <> '') OR (NOT self_approval AND self_approval_justification IS NULL)");
                    table.ForeignKey(
                        name: "fk_playbook_reviews_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playbook_reviews_playbook_versions_playbook_id_version_id",
                        columns: x => new { x.playbook_id, x.version_id },
                        principalSchema: "app",
                        principalTable: "playbook_versions",
                        principalColumns: new[] { "playbook_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_playbooks_id_current_published_version_id",
                schema: "app",
                table: "playbooks",
                columns: new[] { "id", "current_published_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_versions_category_id",
                schema: "app",
                table: "playbook_versions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_versions_playbook_id_source_version_id",
                schema: "app",
                table: "playbook_versions",
                columns: new[] { "playbook_id", "source_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_versions_submitted_by_user_id",
                schema: "app",
                table: "playbook_versions",
                column: "submitted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_playbook_active_draft",
                schema: "app",
                table: "playbook_versions",
                column: "playbook_id",
                unique: true,
                filter: "status IN ('DRAFT','IN_REVIEW')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_version_metadata",
                schema: "app",
                table: "playbook_versions",
                sql: "(status = 'DRAFT' AND review_request_id IS NULL AND submitted_by_user_id IS NULL AND published_at IS NULL) OR (status = 'IN_REVIEW' AND review_request_id IS NOT NULL AND submitted_by_user_id IS NOT NULL AND published_at IS NULL) OR (status IN ('PUBLISHED','ARCHIVED') AND review_request_id IS NOT NULL AND submitted_by_user_id IS NOT NULL AND published_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_version_source",
                schema: "app",
                table: "playbook_versions",
                sql: "source_version_id IS NULL OR source_version_id <> id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_version_state",
                schema: "app",
                table: "playbook_versions",
                sql: "version_number > 0 AND status IN ('DRAFT','IN_REVIEW','PUBLISHED','ARCHIVED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_version_title",
                schema: "app",
                table: "playbook_versions",
                sql: "btrim(title) <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_reviews_actor_user_id",
                schema: "app",
                table: "playbook_reviews",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playbook_reviews_playbook_id_revision",
                schema: "app",
                table: "playbook_reviews",
                columns: new[] { "playbook_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbook_reviews_playbook_id_version_id",
                schema: "app",
                table: "playbook_reviews",
                columns: new[] { "playbook_id", "version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_playbook_reviews_request_id_action",
                schema: "app",
                table: "playbook_reviews",
                columns: new[] { "request_id", "action" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_playbook_review_decision",
                schema: "app",
                table: "playbook_reviews",
                column: "request_id",
                unique: true,
                filter: "action IN ('PUBLISHED','REJECTED')");

            migrationBuilder.AddForeignKey(
                name: "fk_playbook_versions_asp_net_users_submitted_by_user_id",
                schema: "app",
                table: "playbook_versions",
                column: "submitted_by_user_id",
                principalSchema: "auth",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_playbook_versions_incident_categories_category_id",
                schema: "app",
                table: "playbook_versions",
                column: "category_id",
                principalSchema: "app",
                principalTable: "incident_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_playbook_versions_playbook_versions_playbook_id_source_vers",
                schema: "app",
                table: "playbook_versions",
                columns: new[] { "playbook_id", "source_version_id" },
                principalSchema: "app",
                principalTable: "playbook_versions",
                principalColumns: new[] { "playbook_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_playbook_current_version",
                schema: "app",
                table: "playbooks",
                columns: new[] { "id", "current_published_version_id" },
                principalSchema: "app",
                principalTable: "playbook_versions",
                principalColumns: new[] { "playbook_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION app.protect_playbook_version_identity() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Playbook versions cannot be physically removed' USING ERRCODE='42501'; END IF;
                 IF NEW.id IS DISTINCT FROM OLD.id OR NEW.playbook_id IS DISTINCT FROM OLD.playbook_id
                    OR NEW.version_number IS DISTINCT FROM OLD.version_number OR NEW.source_version_id IS DISTINCT FROM OLD.source_version_id THEN
                  RAISE EXCEPTION 'Playbook version identity is immutable' USING ERRCODE='23514';
                 END IF;
                 IF (OLD.status <> 'DRAFT' OR OLD.published_at IS NOT NULL) AND
                    (NEW.title IS DISTINCT FROM OLD.title OR NEW.category_id IS DISTINCT FROM OLD.category_id
                     OR NEW.objective IS DISTINCT FROM OLD.objective OR NEW.instructions IS DISTINCT FROM OLD.instructions) THEN
                  RAISE EXCEPTION 'Reviewed or published content is immutable' USING ERRCODE='23514';
                 END IF;
                 IF NEW.status <> OLD.status AND NOT ((OLD.status='DRAFT' AND NEW.status='IN_REVIEW')
                    OR (OLD.status='IN_REVIEW' AND NEW.status IN ('DRAFT','PUBLISHED'))
                    OR (OLD.status='PUBLISHED' AND NEW.status='ARCHIVED')) THEN
                  RAISE EXCEPTION 'Illegal playbook version transition' USING ERRCODE='23514';
                 END IF;
                 IF OLD.published_at IS NOT NULL AND (NEW.published_at IS DISTINCT FROM OLD.published_at
                    OR NEW.review_request_id IS DISTINCT FROM OLD.review_request_id OR NEW.submitted_by_user_id IS DISTINCT FROM OLD.submitted_by_user_id) THEN
                  RAISE EXCEPTION 'Publication metadata is immutable' USING ERRCODE='23514';
                 END IF;
                 IF OLD.status='IN_REVIEW' AND NEW.status <> 'DRAFT' AND
                   (NEW.review_request_id IS DISTINCT FROM OLD.review_request_id OR NEW.submitted_by_user_id IS DISTINCT FROM OLD.submitted_by_user_id) THEN
                  RAISE EXCEPTION 'Current review identity is immutable' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;

                CREATE FUNCTION app.protect_playbook_content() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE version uuid; version_status text;
                BEGIN
                 IF TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'Playbook content cannot be truncated' USING ERRCODE='42501'; END IF;
                 IF TG_OP='UPDATE' AND (NEW.id IS DISTINCT FROM OLD.id OR NEW.version_id IS DISTINCT FROM OLD.version_id) THEN
                  RAISE EXCEPTION 'Content identity is immutable' USING ERRCODE='23514';
                 END IF;
                 IF TG_OP='DELETE' THEN version=OLD.version_id; ELSE version=NEW.version_id; END IF;
                 SELECT status INTO version_status FROM app.playbook_versions WHERE id=version FOR UPDATE;
                 IF version_status IS NOT NULL AND version_status <> 'DRAFT' THEN
                  RAISE EXCEPTION 'Only draft content can change' USING ERRCODE='23514';
                 END IF;
                 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END; $body$;
                CREATE TRIGGER protect_playbook_steps_content BEFORE INSERT OR UPDATE OR DELETE ON app.playbook_steps FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_content();
                CREATE TRIGGER protect_playbook_dependencies_content BEFORE INSERT OR UPDATE OR DELETE ON app.playbook_step_dependencies FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_content();
                CREATE TRIGGER protect_playbook_requirements_content BEFORE INSERT OR UPDATE OR DELETE ON app.playbook_capability_requirements FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_content();
                CREATE TRIGGER protect_playbook_steps_truncate BEFORE TRUNCATE ON app.playbook_steps FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_content();
                CREATE TRIGGER protect_playbook_dependencies_truncate BEFORE TRUNCATE ON app.playbook_step_dependencies FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_content();
                CREATE TRIGGER protect_playbook_requirements_truncate BEFORE TRUNCATE ON app.playbook_capability_requirements FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_content();

                CREATE FUNCTION app.check_playbook_current_version() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE target uuid;
                BEGIN
                 IF TG_TABLE_NAME='playbooks' THEN target=NEW.id; ELSE target=NEW.playbook_id; END IF;
                 IF EXISTS (SELECT 1 FROM app.playbooks p LEFT JOIN app.playbook_versions v
                     ON v.playbook_id=p.id AND v.id=p.current_published_version_id
                     WHERE p.id=target AND p.current_published_version_id IS NOT NULL AND (v.id IS NULL OR v.status <> 'PUBLISHED')) THEN
                  RAISE EXCEPTION 'Current version must be a publication of this playbook' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER check_playbook_current_version AFTER INSERT OR UPDATE ON app.playbooks
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_playbook_current_version();
                CREATE CONSTRAINT TRIGGER check_playbook_current_version_state AFTER UPDATE ON app.playbook_versions
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.check_playbook_current_version();

                CREATE FUNCTION app.protect_playbook_reviews() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RAISE EXCEPTION 'Playbook reviews are append-only' USING ERRCODE='42501'; END; $body$;
                CREATE TRIGGER protect_playbook_reviews BEFORE UPDATE OR DELETE ON app.playbook_reviews FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_reviews();
                CREATE TRIGGER protect_playbook_reviews_truncate BEFORE TRUNCATE ON app.playbook_reviews FOR EACH STATEMENT EXECUTE FUNCTION app.protect_playbook_reviews();

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.playbooks) OR EXISTS(SELECT 1 FROM app.playbook_reviews) OR EXISTS(SELECT 1 FROM app.playbook_versions WHERE version_number <> 1 OR status <> 'DRAFT' OR source_version_id IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM app.playbooks WHERE current_published_version_id IS NOT NULL) THEN
                  RAISE EXCEPTION 'Cannot roll back playbook drafts/publication while playbook or lifecycle data exists';
                 END IF;
                END; $guard$;
                DROP TRIGGER check_playbook_current_version ON app.playbooks;
                DROP TRIGGER check_playbook_current_version_state ON app.playbook_versions;
                DROP FUNCTION app.check_playbook_current_version();
                DROP TRIGGER protect_playbook_steps_content ON app.playbook_steps;
                DROP TRIGGER protect_playbook_dependencies_content ON app.playbook_step_dependencies;
                DROP TRIGGER protect_playbook_requirements_content ON app.playbook_capability_requirements;
                DROP TRIGGER protect_playbook_steps_truncate ON app.playbook_steps;
                DROP TRIGGER protect_playbook_dependencies_truncate ON app.playbook_step_dependencies;
                DROP TRIGGER protect_playbook_requirements_truncate ON app.playbook_capability_requirements;
                DROP FUNCTION app.protect_playbook_content();
                DROP TRIGGER protect_playbook_reviews ON app.playbook_reviews;
                DROP TRIGGER protect_playbook_reviews_truncate ON app.playbook_reviews;
                DROP FUNCTION app.protect_playbook_reviews();
                CREATE OR REPLACE FUNCTION app.protect_playbook_version_identity() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                 IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'Playbook versions cannot be physically removed' USING ERRCODE='42501'; END IF;
                 IF NEW.id IS DISTINCT FROM OLD.id OR NEW.playbook_id IS DISTINCT FROM OLD.playbook_id OR NEW.version_number IS DISTINCT FROM OLD.version_number THEN
                  RAISE EXCEPTION 'Playbook version identity is immutable' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_playbook_versions_asp_net_users_submitted_by_user_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_playbook_versions_incident_categories_category_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_playbook_versions_playbook_versions_playbook_id_source_vers",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_playbook_current_version",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropTable(
                name: "playbook_reviews",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_playbooks_id_current_published_version_id",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_playbook_versions_playbook_id_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropIndex(
                name: "ix_playbook_versions_category_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropIndex(
                name: "ix_playbook_versions_playbook_id_source_version_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropIndex(
                name: "ix_playbook_versions_submitted_by_user_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropIndex(
                name: "ux_playbook_active_draft",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_version_metadata",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_version_source",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_version_state",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_version_title",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "current_published_version_id",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "review_request_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "source_version_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.DropColumn(
                name: "title",
                schema: "app",
                table: "playbook_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_version_draft",
                schema: "app",
                table: "playbook_versions",
                sql: "version_number = 1 AND status = 'DRAFT'");
        }
    }
}
