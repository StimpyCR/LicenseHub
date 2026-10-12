using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION licensehub_audit_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'AuditLogs is append-only'; END;
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationCredentials", x => x.Id);
                    table.UniqueConstraint("AK_ApplicationCredentials_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApplicationCredentials_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationFeatures", x => x.Id);
                    table.UniqueConstraint("AK_ApplicationFeatures_ApplicationId_Code", x => new { x.ApplicationId, x.Code });
                    table.UniqueConstraint("AK_ApplicationFeatures_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApplicationFeatures_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.UniqueConstraint("AK_Customers_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Customers_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.UniqueConstraint("AK_IdempotencyRecords_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.UniqueConstraint("AK_NotificationOutbox_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    OffsetSeconds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ShortDurationOffsetSeconds = table.Column<int[]>(type: "integer[]", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InAppEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                    table.UniqueConstraint("AK_NotificationSettings_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_NotificationSettings_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                    table.UniqueConstraint("AK_NotificationTemplates_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_NotificationTemplates_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationUserGrants",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUserGrants", x => new { x.UserId, x.ApplicationId, x.Capability });
                    table.ForeignKey(
                        name: "FK_ApplicationUserGrants_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationUserGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DurationUnit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DurationValue = table.Column<int>(type: "integer", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsLifetime = table.Column<bool>(type: "boolean", nullable: false),
                    EntitlementVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.UniqueConstraint("AK_Licenses_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_License_Active", "\"State\" <> 'Active' OR (\"ActivatedAt\" IS NOT NULL AND (\"IsLifetime\" OR \"ExpiresAt\" IS NOT NULL))");
                    table.CheckConstraint("CK_License_Duration", "(\"DurationValue\" IS NULL AND \"DurationUnit\" IS NULL) OR (\"DurationValue\" IS NOT NULL AND \"DurationUnit\" IS NOT NULL AND \"DurationValue\" > 0 AND \"DurationUnit\" IN ('Minute','Hour','Day','Month','Year'))");
                    table.CheckConstraint("CK_License_Lifetime", "NOT \"IsLifetime\" OR \"ExpiresAt\" IS NULL");
                    table.CheckConstraint("CK_License_StartMode", "\"StartMode\" IN ('OnIssue','OnActivation')");
                    table.CheckConstraint("CK_License_State", "\"State\" IN ('Pending','Active','Suspended','Expired','Revoked','Cancelled')");
                    table.CheckConstraint("CK_License_Time", "\"ExpiresAt\" IS NULL OR (\"StartsAt\" IS NOT NULL AND \"ExpiresAt\" > \"StartsAt\")");
                    table.ForeignKey(
                        name: "FK_Licenses_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Licenses_Customers_ApplicationId_CustomerId",
                        columns: x => new { x.ApplicationId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseActivations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseActivations", x => x.Id);
                    table.UniqueConstraint("AK_LicenseActivations_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LicenseActivations_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseActivations_Customers_ApplicationId_CustomerId",
                        columns: x => new { x.ApplicationId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseActivations_Licenses_ApplicationId_LicenseId",
                        columns: x => new { x.ApplicationId, x.LicenseId },
                        principalTable: "Licenses",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseFeatures", x => x.Id);
                    table.UniqueConstraint("AK_LicenseFeatures_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LicenseFeatures_ApplicationFeatures_ApplicationId_FeatureCo~",
                        columns: x => new { x.ApplicationId, x.FeatureCode },
                        principalTable: "ApplicationFeatures",
                        principalColumns: new[] { "ApplicationId", "Code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseFeatures_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseFeatures_Licenses_ApplicationId_LicenseId",
                        columns: x => new { x.ApplicationId, x.LicenseId },
                        principalTable: "Licenses",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntitlementVersion = table.Column<long>(type: "bigint", nullable: false),
                    OffsetSeconds = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastErrorRedacted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.UniqueConstraint("AK_NotificationLogs_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Customers_ApplicationId_CustomerId",
                        columns: x => new { x.ApplicationId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Licenses_ApplicationId_LicenseId",
                        columns: x => new { x.ApplicationId, x.LicenseId },
                        principalTable: "Licenses",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RenewalIntentJson = table.Column<string>(type: "jsonb", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_Payment_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Payment_State", "\"State\" IN ('Pending','Confirmed','Rejected','Refunded')");
                    table.ForeignKey(
                        name: "FK_Payments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Customers_ApplicationId_CustomerId",
                        columns: x => new { x.ApplicationId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Licenses_ApplicationId_LicenseId",
                        columns: x => new { x.ApplicationId, x.LicenseId },
                        principalTable: "Licenses",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseRenewals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AfterExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BeforeLifetime = table.Column<bool>(type: "boolean", nullable: false),
                    AfterLifetime = table.Column<bool>(type: "boolean", nullable: false),
                    Reactivated = table.Column<bool>(type: "boolean", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseRenewals", x => x.Id);
                    table.UniqueConstraint("AK_LicenseRenewals_ApplicationId_Id", x => new { x.ApplicationId, x.Id });
                    table.ForeignKey(
                        name: "FK_LicenseRenewals_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRenewals_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRenewals_Licenses_ApplicationId_LicenseId",
                        columns: x => new { x.ApplicationId, x.LicenseId },
                        principalTable: "Licenses",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRenewals_Payments_ApplicationId_PaymentId",
                        columns: x => new { x.ApplicationId, x.PaymentId },
                        principalTable: "Payments",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCredentials_ApplicationId_Id",
                table: "ApplicationCredentials",
                columns: new[] { "ApplicationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Slug",
                table: "Applications",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserGrants_ApplicationId",
                table: "ApplicationUserGrants",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ApplicationId_CreatedAt_Id",
                table: "AuditLogs",
                columns: new[] { "ApplicationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ApplicationId_SubjectId",
                table: "Customers",
                columns: new[] { "ApplicationId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ApplicationId_PrincipalId_Operation_Key",
                table: "IdempotencyRecords",
                columns: new[] { "ApplicationId", "PrincipalId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_RetainUntil",
                table: "IdempotencyRecords",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseActivations_ApplicationId_CustomerId",
                table: "LicenseActivations",
                columns: new[] { "ApplicationId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseActivations_ApplicationId_LicenseId",
                table: "LicenseActivations",
                columns: new[] { "ApplicationId", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseActivations_LicenseId",
                table: "LicenseActivations",
                column: "LicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFeatures_ApplicationId_FeatureCode",
                table: "LicenseFeatures",
                columns: new[] { "ApplicationId", "FeatureCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFeatures_ApplicationId_LicenseId",
                table: "LicenseFeatures",
                columns: new[] { "ApplicationId", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseFeatures_LicenseId_FeatureCode",
                table: "LicenseFeatures",
                columns: new[] { "LicenseId", "FeatureCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRenewals_ActorId",
                table: "LicenseRenewals",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRenewals_ApplicationId_LicenseId",
                table: "LicenseRenewals",
                columns: new[] { "ApplicationId", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRenewals_ApplicationId_OperationId",
                table: "LicenseRenewals",
                columns: new[] { "ApplicationId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRenewals_ApplicationId_PaymentId",
                table: "LicenseRenewals",
                columns: new[] { "ApplicationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRenewals_PaymentId",
                table: "LicenseRenewals",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ApplicationId_CustomerId",
                table: "Licenses",
                columns: new[] { "ApplicationId", "CustomerId" },
                unique: true,
                filter: "\"ActivatedAt\" IS NOT NULL AND \"State\" IN ('Active','Suspended')");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ApplicationId_State_ExpiresAt_Id",
                table: "Licenses",
                columns: new[] { "ApplicationId", "State", "ExpiresAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_KeyHash",
                table: "Licenses",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_ApplicationId_CustomerId_ScheduledAt_Id",
                table: "NotificationLogs",
                columns: new[] { "ApplicationId", "CustomerId", "ScheduledAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_ApplicationId_LicenseId",
                table: "NotificationLogs",
                columns: new[] { "ApplicationId", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_LicenseId_EntitlementVersion_EventType_Off~",
                table: "NotificationLogs",
                columns: new[] { "LicenseId", "EntitlementVersion", "EventType", "OffsetSeconds", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_CreatedAt",
                table: "NotificationOutbox",
                column: "CreatedAt",
                filter: "\"ProcessedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSettings_ApplicationId",
                table: "NotificationSettings",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_ApplicationId_EventType_Locale",
                table: "NotificationTemplates",
                columns: new[] { "ApplicationId", "EventType", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ApplicationId_CustomerId",
                table: "Payments",
                columns: new[] { "ApplicationId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ApplicationId_LicenseId",
                table: "Payments",
                columns: new[] { "ApplicationId", "LicenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ApplicationId_Reference",
                table: "Payments",
                columns: new[] { "ApplicationId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ApplicationId_State_CreatedAt_Id",
                table: "Payments",
                columns: new[] { "ApplicationId", "State", "CreatedAt", "Id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER audit_append_only BEFORE UPDATE OR DELETE ON "AuditLogs"
                FOR EACH ROW EXECUTE FUNCTION licensehub_audit_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER audit_append_only ON \"AuditLogs\"; DROP FUNCTION licensehub_audit_append_only();");

            migrationBuilder.DropTable(
                name: "ApplicationCredentials");

            migrationBuilder.DropTable(
                name: "ApplicationUserGrants");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "LicenseActivations");

            migrationBuilder.DropTable(
                name: "LicenseFeatures");

            migrationBuilder.DropTable(
                name: "LicenseRenewals");

            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ApplicationFeatures");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Licenses");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Applications");
        }
    }
}
