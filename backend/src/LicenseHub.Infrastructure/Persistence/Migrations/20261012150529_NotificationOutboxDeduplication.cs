using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationOutboxDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_ApplicationId_AggregateId_AggregateVersi~",
                table: "NotificationOutbox",
                columns: new[] { "ApplicationId", "AggregateId", "AggregateVersion", "EventType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_ApplicationId_AggregateId_AggregateVersi~",
                table: "NotificationOutbox");
        }
    }
}
