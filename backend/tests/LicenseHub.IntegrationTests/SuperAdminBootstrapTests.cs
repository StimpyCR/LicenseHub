using System.Buffers.Binary;
using System.Security.Cryptography;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LicenseHub.IntegrationTests;

public sealed class SuperAdminBootstrapTests
{
    [Fact]
    public async Task RequiresVerifiedMfaAndCommitsOnlyOneConcurrentBootstrap()
    {
        var connection = Environment.GetEnvironmentVariable("LICENSEHUB_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Configura PostgreSQL de integración.");
        var original = new NpgsqlConnectionStringBuilder(connection);
        if (original.Database?.EndsWith("_tests", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Requiere base _tests.");
        var databaseName = "bootstrap_" + Guid.NewGuid().ToString("N") + "_tests";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            original.Database = databaseName;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:LicenseHub", original.ConnectionString);
                builder.UseSetting("Jobs:Enabled", "false");
            });
            await using (var scope = factory.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();

            // Aleatorio, efímero y exclusivo de pruebas. No hay credenciales predeterminadas.
            var password = "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var email = Guid.NewGuid().ToString("N") + "@example.invalid";
            var key = Base32(RandomNumberGenerator.GetBytes(20));
            var code = Totp(key);
            var wrongCode = ((int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) + 500000) % 1000000)
                .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ISuperAdminBootstrap>();
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync(
                    new() { Email = email, Password = password, AuthenticatorKey = key, Code = wrongCode }, default));
                Assert.Equal("bootstrap_invalid_mfa", failure.Message);
            }
            await using (var scope = factory.Services.CreateAsyncScope())
                Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Users.CountAsync());

            async Task<bool> AttemptAsync()
            {
                await using var scope = factory.Services.CreateAsyncScope();
                try
                {
                    await scope.ServiceProvider.GetRequiredService<ISuperAdminBootstrap>().InitializeAsync(
                        new() { Email = email, Password = password, AuthenticatorKey = key, Code = Totp(key) }, default);
                    return true;
                }
                catch (InvalidOperationException exception) when (exception.Message == "bootstrap_already_initialized")
                {
                    return false;
                }
            }
            var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
            Assert.Single(outcomes, x => x);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
                var user = await db.Users.SingleAsync();
                Assert.True(user.TwoFactorEnabled);
                Assert.True(user.LockoutEnabled);
                Assert.False(user.EmailConfirmed);
                Assert.NotEqual(password, user.PasswordHash);
                Assert.True(await users.CheckPasswordAsync(user, password));
                Assert.True(await users.IsInRoleAsync(user, "SuperAdmin"));
                Assert.Equal(1, await db.Set<LicenseHub.Infrastructure.Persistence.Records.AuditLogRow>()
                    .CountAsync(x => x.Action == "admin.bootstrap"));
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    internal static string Base32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        return string.Concat(Enumerable.Range(0, bits.Length / 5).Select(i => alphabet[Convert.ToInt32(bits.Substring(i * 5, 5), 2)]));
    }

    internal static string Totp(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(key.Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes, counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (value % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
