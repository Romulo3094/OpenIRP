-- Apply as object owner/migrator, in the intended database, after BasicAdministrativeAudit.
-- Set session parameter openirp.runtime_role to an existing dedicated runtime role first.
-- Creates no users, passwords or business roles; does not grant membership in other SQL roles.
DO $permissions$
DECLARE
    runtime_name text := current_setting('openirp.runtime_role');
    runtime_id oid;
BEGIN
    SELECT oid INTO runtime_id FROM pg_roles
    WHERE rolname = runtime_name AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole
      AND NOT rolreplication AND NOT rolbypassrls AND NOT rolinherit;
    IF runtime_id IS NULL
       OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member = runtime_id)
       OR EXISTS (SELECT 1 FROM pg_database WHERE datdba = runtime_id)
       OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = runtime_id)
       OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = runtime_id) THEN
        RAISE EXCEPTION 'Runtime must be a dedicated NOINHERIT role without ownership, membership or administrative attributes';
    END IF;
    IF to_regclass('audit.audit_events') IS NULL THEN RAISE EXCEPTION 'Apply audit migration first'; END IF;
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), runtime_name);
    EXECUTE format('GRANT USAGE ON SCHEMA auth, app TO %I', runtime_name);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA auth, app TO %I', runtime_name);
    EXECUTE format('GRANT USAGE ON ALL SEQUENCES IN SCHEMA auth, app TO %I', runtime_name);
    IF to_regclass('app.incident_capability_assessments') IS NOT NULL THEN
        EXECUTE format('REVOKE ALL ON TABLE app.incident_capability_assessments FROM %I', runtime_name);
        EXECUTE format('GRANT SELECT, INSERT ON TABLE app.incident_capability_assessments TO %I', runtime_name);
        EXECUTE format('GRANT UPDATE (availability, limitations, reason, assessed_at_revision) ON TABLE app.incident_capability_assessments TO %I', runtime_name);
    END IF;
    IF to_regclass('app.incident_assets') IS NOT NULL THEN
        EXECUTE format('REVOKE ALL ON TABLE app.incident_assets FROM %I', runtime_name);
        EXECUTE format('GRANT SELECT, INSERT ON TABLE app.incident_assets TO %I', runtime_name);
        EXECUTE format('GRANT UPDATE (asset_id, linked_at_revision) ON TABLE app.incident_assets TO %I', runtime_name);
    END IF;
    IF to_regclass('app.incident_events') IS NOT NULL THEN
        EXECUTE format('REVOKE ALL ON TABLE app.incident_events FROM %I', runtime_name);
        EXECUTE format('GRANT SELECT, INSERT ON TABLE app.incident_events TO %I', runtime_name);
    END IF;
    EXECUTE format('REVOKE ALL ON SCHEMA audit FROM %I', runtime_name);
    EXECUTE format('GRANT USAGE ON SCHEMA audit TO %I', runtime_name);
    EXECUTE format('REVOKE ALL ON TABLE audit.audit_events FROM %I', runtime_name);
    EXECUTE format('GRANT SELECT, INSERT ON TABLE audit.audit_events TO %I', runtime_name);
    EXECUTE format('REVOKE ALL ON SEQUENCE audit.audit_events_id_seq FROM %I', runtime_name);
    EXECUTE format('GRANT USAGE ON SEQUENCE audit.audit_events_id_seq TO %I', runtime_name);
END;
$permissions$;
