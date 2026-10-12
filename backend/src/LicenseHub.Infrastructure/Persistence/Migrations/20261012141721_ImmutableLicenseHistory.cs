using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImmutableLicenseHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION licensehub_history_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'License history is append-only'; END;
                $$;
                CREATE TRIGGER activation_append_only BEFORE UPDATE OR DELETE ON "LicenseActivations"
                FOR EACH ROW EXECUTE FUNCTION licensehub_history_append_only();
                CREATE TRIGGER renewal_append_only BEFORE UPDATE OR DELETE ON "LicenseRenewals"
                FOR EACH ROW EXECUTE FUNCTION licensehub_history_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER activation_append_only ON "LicenseActivations";
                DROP TRIGGER renewal_append_only ON "LicenseRenewals";
                DROP FUNCTION licensehub_history_append_only();
                """);
        }
    }
}
