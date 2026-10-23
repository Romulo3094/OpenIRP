using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIncidentRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                    table.UniqueConstraint("ak_incidents_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_incident_revision", "revision > 0");
                    table.CheckConstraint("ck_incident_status", "status = 'REGISTERED'");
                    table.CheckConstraint("ck_incident_title", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "fk_incidents_incident_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "app",
                        principalTable: "incident_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incidents_organization_memberships_organization_id_assigned",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incidents_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_events", x => x.id);
                    table.CheckConstraint("ck_incident_event_action", "(action = 'REGISTERED' AND revision = 1 AND reason IS NULL) OR (action IN ('UPDATED','ASSIGNED') AND revision > 1 AND reason IS NOT NULL AND btrim(reason) <> '')");
                    table.CheckConstraint("ck_incident_event_revision", "revision > 0");
                    table.CheckConstraint("ck_incident_event_status", "status = 'REGISTERED'");
                    table.CheckConstraint("ck_incident_event_title", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "fk_incident_events_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalSchema: "auth",
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_events_incident_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "app",
                        principalTable: "incident_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_events_incidents_organization_id_incident_id",
                        columns: x => new { x.organization_id, x.incident_id },
                        principalSchema: "app",
                        principalTable: "incidents",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_events_organization_memberships_organization_id_as",
                        columns: x => new { x.organization_id, x.assigned_membership_id },
                        principalSchema: "app",
                        principalTable: "organization_memberships",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_actor_user_id",
                schema: "app",
                table: "incident_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_category_id",
                schema: "app",
                table: "incident_events",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_organization_id_assigned_membership_id",
                schema: "app",
                table: "incident_events",
                columns: new[] { "organization_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_organization_id_incident_id_revision",
                schema: "app",
                table: "incident_events",
                columns: new[] { "organization_id", "incident_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidents_category_id",
                schema: "app",
                table: "incidents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_organization_id_assigned_membership_id",
                schema: "app",
                table: "incidents",
                columns: new[] { "organization_id", "assigned_membership_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION app.reject_incident_event_mutation() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $body$
                BEGIN
                    RAISE EXCEPTION 'Incident events are append-only' USING ERRCODE = '42501';
                END;
                $body$;
                CREATE TRIGGER incident_events_no_mutation
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON app.incident_events
                    FOR EACH STATEMENT EXECUTE FUNCTION app.reject_incident_event_mutation();
                REVOKE ALL ON TABLE app.incident_events FROM PUBLIC;
                REVOKE ALL ON FUNCTION app.reject_incident_event_mutation() FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "incidents",
                schema: "app");
            migrationBuilder.Sql("DROP FUNCTION app.reject_incident_event_mutation();");
        }
    }
}
