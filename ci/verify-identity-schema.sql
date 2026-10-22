\set ON_ERROR_STOP on

-- Integration checks on an EPHEMERAL PostgreSQL 17 database only.
-- This script intentionally rolls back all inserted fixtures.
BEGIN;

DO $checks$
DECLARE
    u1 uuid := '11111111-1111-4111-8111-111111111111';
    u2 uuid := '22222222-2222-4222-8222-222222222222';
    o1 uuid := '33333333-3333-4333-8333-333333333333';
    m1 uuid := '44444444-4444-4444-8444-444444444444';
BEGIN
    IF to_regclass('auth.asp_net_users') IS NULL
       OR to_regclass('app.organizations') IS NULL
       OR to_regclass('app.organization_memberships') IS NULL
       OR to_regclass('app.platform_administrators') IS NULL THEN
        RAISE EXCEPTION 'Identity/organization tables missing';
    END IF;

    INSERT INTO auth.asp_net_users
        (id, is_active, created_at, normalized_email,
         email_confirmed, phone_number_confirmed, two_factor_enabled,
         lockout_enabled, access_failed_count)
    VALUES
        (u1, true, now(), 'FIRST@OPENIRP.TEST',
         false, false, false, false, 0);

    -- A second user cannot reuse the normalized email.
    BEGIN
        INSERT INTO auth.asp_net_users
            (id, is_active, created_at, normalized_email,
             email_confirmed, phone_number_confirmed, two_factor_enabled,
             lockout_enabled, access_failed_count)
        VALUES (u2, true, now(), 'FIRST@OPENIRP.TEST',
                false, false, false, false, 0);
        RAISE EXCEPTION 'Expected normalized-email uniqueness violation';
    EXCEPTION WHEN unique_violation THEN
        NULL; -- Expected.
    END;

    INSERT INTO app.organizations
        (id, name, slug, country_code, status, created_by_user_id, created_at)
    VALUES (o1, 'CI test org', 'ci-test-org', 'CL', 'ACTIVE', u1, now());

    -- Slugs are globally unique.
    BEGIN
        INSERT INTO app.organizations
            (id, name, slug, country_code, status, created_by_user_id, created_at)
        VALUES (gen_random_uuid(), 'Duplicate', 'ci-test-org', 'CL', 'ACTIVE', u1, now());
        RAISE EXCEPTION 'Expected duplicate slug to be rejected';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;

    -- Organization CHECK constraints must be enforced in PostgreSQL.
    BEGIN
        INSERT INTO app.organizations
            (id, name, slug, country_code, status, created_by_user_id, created_at)
        VALUES (gen_random_uuid(), 'Invalid', 'ci-invalid-org', 'chile', 'ACTIVE', u1, now());
        RAISE EXCEPTION 'Expected country-code check violation';
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    INSERT INTO app.organization_memberships
        (id, organization_id, user_id, role, status, created_at)
    VALUES (m1, o1, u1, 'ORG_ADMIN', 'ACTIVE', now());

    -- Cannot duplicate an organization/user membership.
    BEGIN
        INSERT INTO app.organization_memberships
            (id, organization_id, user_id, role, status, created_at)
        VALUES (gen_random_uuid(), o1, u1, 'ANALYST', 'ACTIVE', now());
        RAISE EXCEPTION 'Expected duplicate membership to be rejected';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;

    -- Unknown organizational roles must be rejected.
    BEGIN
        INSERT INTO app.organization_memberships
            (id, organization_id, user_id, role, status, created_at)
        VALUES (gen_random_uuid(), o1, u2, 'SUPERADMIN', 'ACTIVE', now());
        RAISE EXCEPTION 'Expected unknown organizational role to be rejected';
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    -- A revoked membership must include its revocation timestamp.
    BEGIN
        UPDATE app.organization_memberships
        SET status = 'REVOKED', revoked_at = NULL
        WHERE id = m1;
        RAISE EXCEPTION 'Expected revoked membership check to reject null timestamp';
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    -- A referenced user cannot be deleted by accident.
    BEGIN
        DELETE FROM auth.asp_net_users WHERE id = u1;
        RAISE EXCEPTION 'Expected creator/membership FK to reject user deletion';
    EXCEPTION WHEN foreign_key_violation THEN NULL;
    END;

    RAISE NOTICE 'All initial Identity/organization schema checks passed';
END
$checks$;

ROLLBACK;
