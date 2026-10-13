-- Ejecutar por el dueño de esquemas tras migración/provisionamiento.
-- psql -v runtime_role=licensehub_runtime -v database_name=licensehub ...
-- El rol debe existir, no ser dueño, superusuario ni miembro del rol migrador.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON DATABASE :"database_name" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database_name" TO :"runtime_role";
GRANT USAGE ON SCHEMA public, hangfire TO :"runtime_role";
REVOKE DELETE ON ALL TABLES IN SCHEMA public FROM :"runtime_role";
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO :"runtime_role";
GRANT DELETE ON "AdminMfaUses", "AdminAuthChallenges", "AdminSessions", "ApplicationUserGrants",
    "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims" TO :"runtime_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO :"runtime_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public, hangfire TO :"runtime_role";
REVOKE UPDATE, DELETE ON "AuditLogs", "LicenseRenewals", "LicenseActivations" FROM :"runtime_role";
REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM :"runtime_role";
-- No CREATE, TRUNCATE, TRIGGER, permisos de roles ni ALTER DEFAULT PRIVILEGES.
-- Reaplicar con revisión después de migraciones que agreguen tablas/secuencias.
