-- Solo lectura y ensayos con rollback en una base creada para restauración.
BEGIN;
DO $$
BEGIN
    IF current_database() !~ '^licensehub_restore_[a-f0-9]{32}_tests$' THEN
        RAISE EXCEPTION 'Restoration checks require an isolated restore database';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE contype IN ('f', 'c') AND NOT convalidated) THEN
        RAISE EXCEPTION 'Unvalidated constraints';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_index WHERE NOT indisvalid) THEN
        RAISE EXCEPTION 'Invalid indexes';
    END IF;
    IF EXISTS (SELECT 1 FROM "Licenses" WHERE "KeyHash" !~ '^[0-9A-Fa-f]{64}$')
       OR EXISTS (SELECT 1 FROM "ApplicationCredentials" WHERE "SecretHash" !~ '^[0-9A-Fa-f]{64}$') THEN
        RAISE EXCEPTION 'Invalid secret hashes';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "AspNetUsers" u
        JOIN "AspNetUserRoles" ur ON ur."UserId"=u."Id"
        JOIN "AspNetRoles" r ON r."Id"=ur."RoleId"
        WHERE r."Name"='SuperAdmin' AND NOT u."TwoFactorEnabled"
    ) THEN RAISE EXCEPTION 'SuperAdmin without MFA'; END IF;
    -- Las FK e índices únicos se recrearon y verificaron durante pg_restore.
    IF EXISTS (SELECT 1 FROM "AuditLogs") THEN
        BEGIN
            UPDATE "AuditLogs" SET "Action"="Action" WHERE "Id"=(SELECT "Id" FROM "AuditLogs" LIMIT 1);
            RAISE EXCEPTION 'Audit update unexpectedly accepted' USING ERRCODE='P0002';
        EXCEPTION WHEN SQLSTATE 'P0001' THEN NULL;
        END;
    END IF;
    IF EXISTS (SELECT 1 FROM "LicenseRenewals") THEN
        BEGIN
            UPDATE "LicenseRenewals" SET "Reason"="Reason" WHERE "Id"=(SELECT "Id" FROM "LicenseRenewals" LIMIT 1);
            RAISE EXCEPTION 'Renewal update unexpectedly accepted' USING ERRCODE='P0002';
        EXCEPTION WHEN SQLSTATE 'P0001' THEN NULL;
        END;
    END IF;
END $$;
ROLLBACK;
