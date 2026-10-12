using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationCredentialHashIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCredentials_SecretHash",
                table: "ApplicationCredentials",
                column: "SecretHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationCredentials_SecretHash",
                table: "ApplicationCredentials");
        }
    }
}
