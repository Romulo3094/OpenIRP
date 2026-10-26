using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookCloning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clone_reason",
                schema: "app",
                table: "playbooks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_revision",
                schema: "app",
                table: "playbooks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_version_id",
                schema: "app",
                table: "playbooks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_playbooks_source_version_id",
                schema: "app",
                table: "playbooks",
                column: "source_version_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_playbook_clone_origin",
                schema: "app",
                table: "playbooks",
                sql: "(source_version_id IS NULL AND source_revision IS NULL AND clone_reason IS NULL) OR (source_version_id IS NOT NULL AND source_revision IS NOT NULL AND source_revision > 0 AND clone_reason IS NOT NULL AND btrim(clone_reason) <> '')");

            migrationBuilder.AddForeignKey(
                name: "fk_playbook_clone_source_version",
                schema: "app",
                table: "playbooks",
                column: "source_version_id",
                principalSchema: "app",
                principalTable: "playbook_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION app.protect_playbook_clone_origin() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE src_playbook uuid; src_org uuid; src_current uuid; src_revision bigint; src_status text; src_published timestamptz;
                BEGIN
                 IF TG_OP='UPDATE' THEN
                  IF NEW.source_version_id IS DISTINCT FROM OLD.source_version_id OR NEW.source_revision IS DISTINCT FROM OLD.source_revision
                     OR NEW.clone_reason IS DISTINCT FROM OLD.clone_reason THEN
                   RAISE EXCEPTION 'Playbook clone origin is immutable' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                 END IF;
                 IF NEW.source_version_id IS NULL THEN RETURN NEW; END IF;
                 SELECT p.id,p.organization_id,p.current_published_version_id,p.revision,v.status,v.published_at
                 INTO src_playbook,src_org,src_current,src_revision,src_status,src_published
                 FROM app.playbook_versions v JOIN app.playbooks p ON p.id=v.playbook_id
                 WHERE v.id=NEW.source_version_id FOR SHARE OF p,v;
                 IF NOT FOUND THEN RAISE EXCEPTION 'Clone source does not exist' USING ERRCODE='23503'; END IF;
                 IF src_playbook=NEW.id OR src_current IS DISTINCT FROM NEW.source_version_id
                    OR src_status <> 'PUBLISHED' OR src_published IS NULL OR src_revision IS DISTINCT FROM NEW.source_revision
                    OR (src_org IS NOT NULL AND src_org IS DISTINCT FROM NEW.organization_id) THEN
                  RAISE EXCEPTION 'Clone source must be current, revision-matched and public or in the same organization' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END; $body$;
                CREATE TRIGGER protect_playbook_clone_origin BEFORE INSERT OR UPDATE ON app.playbooks
                 FOR EACH ROW EXECUTE FUNCTION app.protect_playbook_clone_origin();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                 IF EXISTS(SELECT 1 FROM app.playbooks WHERE source_version_id IS NOT NULL OR source_revision IS NOT NULL OR clone_reason IS NOT NULL) THEN
                  RAISE EXCEPTION 'Cannot roll back playbook cloning while provenance data exists';
                 END IF;
                END; $guard$;
                DROP TRIGGER protect_playbook_clone_origin ON app.playbooks;
                DROP FUNCTION app.protect_playbook_clone_origin();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_playbook_clone_source_version",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropIndex(
                name: "ix_playbooks_source_version_id",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_playbook_clone_origin",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropColumn(
                name: "clone_reason",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropColumn(
                name: "source_revision",
                schema: "app",
                table: "playbooks");

            migrationBuilder.DropColumn(
                name: "source_version_id",
                schema: "app",
                table: "playbooks");
        }
    }
}
