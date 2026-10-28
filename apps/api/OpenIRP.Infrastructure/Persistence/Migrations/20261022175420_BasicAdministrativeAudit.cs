using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BasicAdministrativeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_actor", "actor_type IN ('USER','SYSTEM','ANONYMOUS') AND (actor_type <> 'USER' OR actor_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_audit_details", "jsonb_typeof(details_json) = 'object' AND octet_length(details_json::text) <= 4096");
                    table.CheckConstraint("ck_audit_outcome", "outcome IN ('SUCCESS','DENIED','FAILURE')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_correlation_id",
                schema: "audit",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id_id",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "organization_id", "id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.reject_audit_mutation() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $body$
                BEGIN
                    RAISE EXCEPTION 'Audit events are append-only' USING ERRCODE = '42501';
                END;
                $body$;
                CREATE TRIGGER audit_events_no_mutation
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON audit.audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_audit_mutation();
                REVOKE ALL ON TABLE audit.audit_events FROM PUBLIC;
                REVOKE ALL ON SEQUENCE audit.audit_events_id_seq FROM PUBLIC;
                REVOKE ALL ON FUNCTION audit.reject_audit_mutation() FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");
            migrationBuilder.Sql("DROP FUNCTION audit.reject_audit_mutation();");
        }
    }
}
