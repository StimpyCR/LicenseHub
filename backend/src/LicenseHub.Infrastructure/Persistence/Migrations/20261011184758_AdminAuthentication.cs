using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAuthChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuthChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuthChallenges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminMfaUses",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminMfaUses", x => new { x.UserId, x.TokenHash });
                    table.ForeignKey(
                        name: "FK_AdminMfaUses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuthChallenges_ExpiresAt",
                table: "AdminAuthChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuthChallenges_UserId",
                table: "AdminAuthChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMfaUses_ExpiresAt",
                table: "AdminMfaUses",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_ExpiresAt",
                table: "AdminSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_UserId",
                table: "AdminSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuthChallenges");

            migrationBuilder.DropTable(
                name: "AdminMfaUses");

            migrationBuilder.DropTable(
                name: "AdminSessions");
        }
    }
}
