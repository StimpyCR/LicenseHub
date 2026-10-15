using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using static LicenseHub.IntegrationTests.AdminAuthenticationTests;

namespace LicenseHub.IntegrationTests;

public sealed class RuntimeRoleReadinessTests
{
    [Fact]
    public async Task AdministrativeConnectionIsNotProductionReadyButDevelopmentIsUnchanged()
    {
        await using var fixture = await AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        Assert.True(await new PostgresAvailability(database).CanConnectAsync(default));
        Assert.False(await new PostgresAvailability(database, requireRuntimeRole: true).CanConnectAsync(default));
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("CREATEDB", false)]
    [InlineData("CREATEROLE", false)]
    [InlineData("BYPASSRLS", false)]
    [InlineData("REPLICATION", false)]
    [InlineData("schema", false)]
    [InlineData("database", false)]
    [InlineData("server_files", false)]
    [InlineData("admin_membership", false)]
    [InlineData("owner_membership", false)]
    public async Task ProductionChecksActualRolePrivileges(string privilege, bool expectedReady)
    {
        await using var fixture = await AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        using var identifiers = new NpgsqlCommandBuilder();
        var role = identifiers.QuoteIdentifier("runtime_" + Guid.NewGuid().ToString("N"));
        var databaseName = identifiers.QuoteIdentifier(fixture.DatabaseName);
        var parent = identifiers.QuoteIdentifier("parent_" + Guid.NewGuid().ToString("N"));
        async Task ExecuteAsync(string sql)
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction.GetDbTransaction();
            await command.ExecuteNonQueryAsync();
        }
        // Identifiers are server-generated. All role/schema grants are rolled back.
        await ExecuteAsync($"CREATE ROLE {role} NOLOGIN");
        await ExecuteAsync($"GRANT USAGE ON SCHEMA public TO {role}");
        await ExecuteAsync($"GRANT SELECT ON \"__EFMigrationsHistory\" TO {role}");
        var statement = privilege switch
        {
            "none" => null,
            "CREATEDB" or "CREATEROLE" or "BYPASSRLS" or "REPLICATION" => $"ALTER ROLE {role} {privilege}",
            "schema" => $"GRANT CREATE ON SCHEMA public TO {role}",
            "database" => $"GRANT CREATE ON DATABASE {databaseName} TO {role}",
            "server_files" => $"GRANT pg_write_server_files TO {role}",
            "admin_membership" => $"CREATE ROLE {parent} NOLOGIN CREATEDB; GRANT {parent} TO {role} WITH INHERIT FALSE",
            "owner_membership" => $"CREATE ROLE {parent} NOLOGIN; GRANT CREATE ON DATABASE {databaseName} TO {parent}; GRANT {parent} TO {role} WITH INHERIT FALSE",
            _ => throw new InvalidOperationException("Unknown test privilege.")
        };
        if (statement is not null) await ExecuteAsync(statement);
        await ExecuteAsync($"SET LOCAL ROLE {role}");
        Assert.Equal(expectedReady, await new PostgresAvailability(database, requireRuntimeRole: true).CanConnectAsync(default));
    }
}
