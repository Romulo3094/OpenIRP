using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenIRP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictedEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence",
                sql: "kind IN ('TEXT','REFERENCE','FILE') AND classification IN ('INTERNAL','CONFIDENTIAL','RESTRICTED') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");
            migrationBuilder.Sql("""
CREATE FUNCTION app.check_restricted_evidence() RETURNS trigger LANGUAGE plpgsql AS $guard$
BEGIN
 IF NEW.classification='RESTRICTED' AND NOT EXISTS(SELECT 1 FROM app.organization_memberships m JOIN auth.asp_net_users u ON u.id=m.user_id JOIN app.organizations o ON o.id=m.organization_id WHERE m.organization_id=NEW.organization_id AND m.user_id=NEW.created_by_user_id AND m.role='ORG_ADMIN' AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.is_active AND (u.lockout_end IS NULL OR u.lockout_end<=statement_timestamp())) THEN RAISE EXCEPTION 'Restricted evidence requires local administrator' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END;$guard$;
CREATE TRIGGER check_restricted_evidence BEFORE INSERT ON app.incident_evidence FOR EACH ROW EXECUTE FUNCTION app.check_restricted_evidence();
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DO $down$ BEGIN IF EXISTS(SELECT 1 FROM app.incident_evidence WHERE classification='RESTRICTED') THEN RAISE EXCEPTION 'Cannot remove restricted policy with retained evidence'; END IF; END;$down$;
DROP TRIGGER check_restricted_evidence ON app.incident_evidence;
DROP FUNCTION app.check_restricted_evidence();
""");
            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_content",
                schema: "app",
                table: "incident_evidence",
                sql: "kind IN ('TEXT','REFERENCE','FILE') AND classification IN ('INTERNAL','CONFIDENTIAL') AND btrim(title)<>'' AND btrim(content)<>'' AND (kind<>'REFERENCE' OR (length(content)<=2048 AND content ~ '^https://'))");
        }
    }
}
