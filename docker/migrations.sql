CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE FUNCTION licensehub_audit_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'AuditLogs is append-only'; END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "Applications" (
        "Id" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Slug" character varying(64) NOT NULL,
        "IsActive" boolean NOT NULL,
        CONSTRAINT "PK_Applications" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetRoles" (
        "Id" uuid NOT NULL,
        "Name" character varying(256),
        "NormalizedName" character varying(256),
        "ConcurrencyStamp" text,
        CONSTRAINT "PK_AspNetRoles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetUsers" (
        "Id" uuid NOT NULL,
        "UserName" character varying(256),
        "NormalizedUserName" character varying(256),
        "Email" character varying(256),
        "NormalizedEmail" character varying(256),
        "EmailConfirmed" boolean NOT NULL,
        "PasswordHash" text,
        "SecurityStamp" text,
        "ConcurrencyStamp" text,
        "PhoneNumber" text,
        "PhoneNumberConfirmed" boolean NOT NULL,
        "TwoFactorEnabled" boolean NOT NULL,
        "LockoutEnd" timestamp with time zone,
        "LockoutEnabled" boolean NOT NULL,
        "AccessFailedCount" integer NOT NULL,
        CONSTRAINT "PK_AspNetUsers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "ApplicationCredentials" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "SecretHash" character varying(64) NOT NULL,
        "Scopes" text[] NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "RevokedAt" timestamp with time zone,
        CONSTRAINT "PK_ApplicationCredentials" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ApplicationCredentials_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_ApplicationCredentials_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "ApplicationFeatures" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "Code" character varying(64) NOT NULL,
        "Name" character varying(120) NOT NULL,
        "IsActive" boolean NOT NULL,
        CONSTRAINT "PK_ApplicationFeatures" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ApplicationFeatures_ApplicationId_Code" UNIQUE ("ApplicationId", "Code"),
        CONSTRAINT "AK_ApplicationFeatures_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_ApplicationFeatures_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AuditLogs" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid,
        "ActorId" character varying(128) NOT NULL,
        "Action" character varying(100) NOT NULL,
        "EntityId" uuid,
        "Reason" character varying(1000),
        "CorrelationId" character varying(128) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AuditLogs_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "Customers" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "SubjectId" character varying(128) NOT NULL,
        "DisplayName" character varying(200),
        "Email" character varying(254),
        CONSTRAINT "PK_Customers" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Customers_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_Customers_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "IdempotencyRecords" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "PrincipalId" character varying(128) NOT NULL,
        "Operation" character varying(128) NOT NULL,
        "Key" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "ResponseJson" jsonb,
        "ResponseStatus" integer,
        "State" character varying(16) NOT NULL,
        "RetainUntil" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_IdempotencyRecords_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_IdempotencyRecords_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "NotificationOutbox" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "EventType" character varying(64) NOT NULL,
        "AggregateId" uuid NOT NULL,
        "AggregateVersion" bigint NOT NULL,
        "PayloadJson" jsonb NOT NULL,
        "ProcessedAt" timestamp with time zone,
        "LeaseUntil" timestamp with time zone,
        "Attempts" integer NOT NULL,
        CONSTRAINT "PK_NotificationOutbox" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_NotificationOutbox_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_NotificationOutbox_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "NotificationSettings" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "OffsetSeconds" integer[] NOT NULL,
        "ShortDurationOffsetSeconds" integer[] NOT NULL,
        "EmailEnabled" boolean NOT NULL,
        "InAppEnabled" boolean NOT NULL,
        CONSTRAINT "PK_NotificationSettings" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_NotificationSettings_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_NotificationSettings_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "NotificationTemplates" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "EventType" character varying(32) NOT NULL,
        "Locale" character varying(5) NOT NULL,
        "Subject" character varying(200) NOT NULL,
        "Body" character varying(20000) NOT NULL,
        CONSTRAINT "PK_NotificationTemplates" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_NotificationTemplates_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_NotificationTemplates_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetRoleClaims" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY,
        "RoleId" uuid NOT NULL,
        "ClaimType" text,
        "ClaimValue" text,
        CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "ApplicationUserGrants" (
        "UserId" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "Capability" character varying(64) NOT NULL,
        CONSTRAINT "PK_ApplicationUserGrants" PRIMARY KEY ("UserId", "ApplicationId", "Capability"),
        CONSTRAINT "FK_ApplicationUserGrants_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ApplicationUserGrants_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetUserClaims" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY,
        "UserId" uuid NOT NULL,
        "ClaimType" text,
        "ClaimValue" text,
        CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetUserLogins" (
        "LoginProvider" text NOT NULL,
        "ProviderKey" text NOT NULL,
        "ProviderDisplayName" text,
        "UserId" uuid NOT NULL,
        CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
        CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetUserRoles" (
        "UserId" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
        CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "AspNetUserTokens" (
        "UserId" uuid NOT NULL,
        "LoginProvider" text NOT NULL,
        "Name" text NOT NULL,
        "Value" text,
        CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
        CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "Licenses" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "CustomerId" uuid,
        "KeyHash" character varying(64) NOT NULL,
        "State" character varying(16) NOT NULL,
        "StartMode" character varying(16) NOT NULL,
        "DurationUnit" character varying(16),
        "DurationValue" integer,
        "StartsAt" timestamp with time zone,
        "ActivatedAt" timestamp with time zone,
        "ExpiresAt" timestamp with time zone,
        "IsLifetime" boolean NOT NULL,
        "EntitlementVersion" bigint NOT NULL,
        CONSTRAINT "PK_Licenses" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Licenses_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "CK_License_Active" CHECK ("State" <> 'Active' OR ("ActivatedAt" IS NOT NULL AND ("IsLifetime" OR "ExpiresAt" IS NOT NULL))),
        CONSTRAINT "CK_License_Duration" CHECK (("DurationValue" IS NULL AND "DurationUnit" IS NULL) OR ("DurationValue" IS NOT NULL AND "DurationUnit" IS NOT NULL AND "DurationValue" > 0 AND "DurationUnit" IN ('Minute','Hour','Day','Month','Year'))),
        CONSTRAINT "CK_License_Lifetime" CHECK (NOT "IsLifetime" OR "ExpiresAt" IS NULL),
        CONSTRAINT "CK_License_StartMode" CHECK ("StartMode" IN ('OnIssue','OnActivation')),
        CONSTRAINT "CK_License_State" CHECK ("State" IN ('Pending','Active','Suspended','Expired','Revoked','Cancelled')),
        CONSTRAINT "CK_License_Time" CHECK ("ExpiresAt" IS NULL OR ("StartsAt" IS NOT NULL AND "ExpiresAt" > "StartsAt")),
        CONSTRAINT "FK_Licenses_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Licenses_Customers_ApplicationId_CustomerId" FOREIGN KEY ("ApplicationId", "CustomerId") REFERENCES "Customers" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "LicenseActivations" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "LicenseId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "SubjectId" character varying(128) NOT NULL,
        "ActivatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_LicenseActivations" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_LicenseActivations_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_LicenseActivations_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseActivations_Customers_ApplicationId_CustomerId" FOREIGN KEY ("ApplicationId", "CustomerId") REFERENCES "Customers" ("ApplicationId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseActivations_Licenses_ApplicationId_LicenseId" FOREIGN KEY ("ApplicationId", "LicenseId") REFERENCES "Licenses" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "LicenseFeatures" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "LicenseId" uuid NOT NULL,
        "FeatureCode" character varying(64) NOT NULL,
        CONSTRAINT "PK_LicenseFeatures" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_LicenseFeatures_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_LicenseFeatures_ApplicationFeatures_ApplicationId_FeatureCo~" FOREIGN KEY ("ApplicationId", "FeatureCode") REFERENCES "ApplicationFeatures" ("ApplicationId", "Code") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseFeatures_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseFeatures_Licenses_ApplicationId_LicenseId" FOREIGN KEY ("ApplicationId", "LicenseId") REFERENCES "Licenses" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "NotificationLogs" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "LicenseId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "EventType" character varying(32) NOT NULL,
        "EntitlementVersion" bigint NOT NULL,
        "OffsetSeconds" integer NOT NULL,
        "Channel" character varying(16) NOT NULL,
        "ScheduledAt" timestamp with time zone NOT NULL,
        "State" character varying(16) NOT NULL,
        "Attempts" integer NOT NULL,
        "ProviderId" character varying(256),
        "LastErrorRedacted" character varying(1000),
        "LeaseUntil" timestamp with time zone,
        CONSTRAINT "PK_NotificationLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_NotificationLogs_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_NotificationLogs_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_NotificationLogs_Customers_ApplicationId_CustomerId" FOREIGN KEY ("ApplicationId", "CustomerId") REFERENCES "Customers" ("ApplicationId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_NotificationLogs_Licenses_ApplicationId_LicenseId" FOREIGN KEY ("ApplicationId", "LicenseId") REFERENCES "Licenses" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "Payments" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "LicenseId" uuid NOT NULL,
        "CustomerId" uuid NOT NULL,
        "Amount" numeric(19,4) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "State" character varying(16) NOT NULL,
        "Reference" character varying(120) NOT NULL,
        "RenewalIntentJson" jsonb,
        "ConfirmedAt" timestamp with time zone,
        CONSTRAINT "PK_Payments" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_Payments_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "CK_Payment_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_Payment_State" CHECK ("State" IN ('Pending','Confirmed','Rejected','Refunded')),
        CONSTRAINT "FK_Payments_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_Customers_ApplicationId_CustomerId" FOREIGN KEY ("ApplicationId", "CustomerId") REFERENCES "Customers" ("ApplicationId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Payments_Licenses_ApplicationId_LicenseId" FOREIGN KEY ("ApplicationId", "LicenseId") REFERENCES "Licenses" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TABLE "LicenseRenewals" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "Version" bigint NOT NULL,
        "LicenseId" uuid NOT NULL,
        "OperationId" uuid NOT NULL,
        "PaymentId" uuid,
        "BeforeExpiresAt" timestamp with time zone,
        "AfterExpiresAt" timestamp with time zone,
        "BeforeLifetime" boolean NOT NULL,
        "AfterLifetime" boolean NOT NULL,
        "Reactivated" boolean NOT NULL,
        "ActorId" uuid NOT NULL,
        "Reason" character varying(1000) NOT NULL,
        CONSTRAINT "PK_LicenseRenewals" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_LicenseRenewals_ApplicationId_Id" UNIQUE ("ApplicationId", "Id"),
        CONSTRAINT "FK_LicenseRenewals_Applications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "Applications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseRenewals_AspNetUsers_ActorId" FOREIGN KEY ("ActorId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseRenewals_Licenses_ApplicationId_LicenseId" FOREIGN KEY ("ApplicationId", "LicenseId") REFERENCES "Licenses" ("ApplicationId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_LicenseRenewals_Payments_ApplicationId_PaymentId" FOREIGN KEY ("ApplicationId", "PaymentId") REFERENCES "Payments" ("ApplicationId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_ApplicationCredentials_ApplicationId_Id" ON "ApplicationCredentials" ("ApplicationId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_Applications_Slug" ON "Applications" ("Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_ApplicationUserGrants_ApplicationId" ON "ApplicationUserGrants" ("ApplicationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_AuditLogs_ApplicationId_CreatedAt_Id" ON "AuditLogs" ("ApplicationId", "CreatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_Customers_ApplicationId_SubjectId" ON "Customers" ("ApplicationId", "SubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_IdempotencyRecords_ApplicationId_PrincipalId_Operation_Key" ON "IdempotencyRecords" ("ApplicationId", "PrincipalId", "Operation", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_IdempotencyRecords_RetainUntil" ON "IdempotencyRecords" ("RetainUntil");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseActivations_ApplicationId_CustomerId" ON "LicenseActivations" ("ApplicationId", "CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseActivations_ApplicationId_LicenseId" ON "LicenseActivations" ("ApplicationId", "LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_LicenseActivations_LicenseId" ON "LicenseActivations" ("LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseFeatures_ApplicationId_FeatureCode" ON "LicenseFeatures" ("ApplicationId", "FeatureCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseFeatures_ApplicationId_LicenseId" ON "LicenseFeatures" ("ApplicationId", "LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_LicenseFeatures_LicenseId_FeatureCode" ON "LicenseFeatures" ("LicenseId", "FeatureCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseRenewals_ActorId" ON "LicenseRenewals" ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseRenewals_ApplicationId_LicenseId" ON "LicenseRenewals" ("ApplicationId", "LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_LicenseRenewals_ApplicationId_OperationId" ON "LicenseRenewals" ("ApplicationId", "OperationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_LicenseRenewals_ApplicationId_PaymentId" ON "LicenseRenewals" ("ApplicationId", "PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_LicenseRenewals_PaymentId" ON "LicenseRenewals" ("PaymentId") WHERE "PaymentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_Licenses_ApplicationId_CustomerId" ON "Licenses" ("ApplicationId", "CustomerId") WHERE "ActivatedAt" IS NOT NULL AND "State" IN ('Active','Suspended');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_Licenses_ApplicationId_State_ExpiresAt_Id" ON "Licenses" ("ApplicationId", "State", "ExpiresAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_Licenses_KeyHash" ON "Licenses" ("KeyHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_NotificationLogs_ApplicationId_CustomerId_ScheduledAt_Id" ON "NotificationLogs" ("ApplicationId", "CustomerId", "ScheduledAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_NotificationLogs_ApplicationId_LicenseId" ON "NotificationLogs" ("ApplicationId", "LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_NotificationLogs_LicenseId_EntitlementVersion_EventType_Off~" ON "NotificationLogs" ("LicenseId", "EntitlementVersion", "EventType", "OffsetSeconds", "Channel");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_NotificationOutbox_CreatedAt" ON "NotificationOutbox" ("CreatedAt") WHERE "ProcessedAt" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_NotificationSettings_ApplicationId" ON "NotificationSettings" ("ApplicationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_NotificationTemplates_ApplicationId_EventType_Locale" ON "NotificationTemplates" ("ApplicationId", "EventType", "Locale");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_Payments_ApplicationId_CustomerId" ON "Payments" ("ApplicationId", "CustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_Payments_ApplicationId_LicenseId" ON "Payments" ("ApplicationId", "LicenseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_Payments_ApplicationId_Reference" ON "Payments" ("ApplicationId", "Reference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE INDEX "IX_Payments_ApplicationId_State_CreatedAt_Id" ON "Payments" ("ApplicationId", "State", "CreatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    CREATE TRIGGER audit_append_only BEFORE UPDATE OR DELETE ON "AuditLogs"
    FOR EACH ROW EXECUTE FUNCTION licensehub_audit_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011161920_InitialSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011161920_InitialSchema', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE TABLE "AdminAuthChallenges" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SecurityStamp" character varying(256) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "ConsumedAt" timestamp with time zone,
        CONSTRAINT "PK_AdminAuthChallenges" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AdminAuthChallenges_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE TABLE "AdminMfaUses" (
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(64) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AdminMfaUses" PRIMARY KEY ("UserId", "TokenHash"),
        CONSTRAINT "FK_AdminMfaUses_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE TABLE "AdminSessions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SecurityStamp" character varying(256) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "RevokedAt" timestamp with time zone,
        CONSTRAINT "PK_AdminSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AdminSessions_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE INDEX "IX_AdminAuthChallenges_ExpiresAt" ON "AdminAuthChallenges" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE INDEX "IX_AdminAuthChallenges_UserId" ON "AdminAuthChallenges" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE INDEX "IX_AdminMfaUses_ExpiresAt" ON "AdminMfaUses" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE INDEX "IX_AdminSessions_ExpiresAt" ON "AdminSessions" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    CREATE INDEX "IX_AdminSessions_UserId" ON "AdminSessions" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011184758_AdminAuthentication') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011184758_AdminAuthentication', '10.0.12');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011193227_IntegrationCredentialHashIndex') THEN
    CREATE UNIQUE INDEX "IX_ApplicationCredentials_SecretHash" ON "ApplicationCredentials" ("SecretHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261011193227_IntegrationCredentialHashIndex') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261011193227_IntegrationCredentialHashIndex', '10.0.12');
    END IF;
END $EF$;
COMMIT;

