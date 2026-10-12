using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace LicenseHub.IntegrationTests;

public sealed class AdminAuthenticationTests
{
    [Fact]
    public async Task HttpRequiresCsrfAndMfaAndRevokesCopiedCookieOnLogout()
    {
        await using var fixture = await AuthFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var seed = await client.GetAsync("/api/v1/admin/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        AssertSecureCookie(seed, "__Host-LicenseHubCsrf");
        var csrf = await TokenAsync(seed);
        var login = new { email = fixture.Email, password = fixture.Password };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/auth/login", login)).StatusCode);
        var failed = await PostAsync(client, "/login", new { email = fixture.Email, password = "invalid" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        Assert.DoesNotContain(fixture.Email, await failed.Content.ReadAsStringAsync());
        var response = await PostAsync(client, "/login", login, csrf);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AssertSecureCookie(response, "__Host-LicenseHubChallenge");
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-LicenseHub=", StringComparison.Ordinal));
        using (var challengeJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.True(challengeJson.RootElement.GetProperty("mfaRequired").GetBoolean());
            Assert.EndsWith("Z", challengeJson.RootElement.GetProperty("challengeExpiresAt").GetString());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/mfa", new { code = "abc123" }, csrf)).StatusCode);
        var complete = await PostAsync(client, "/mfa", new { code = SuperAdminBootstrapTests.Totp(fixture.Key) }, csrf);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        AssertSecureCookie(complete, "__Host-LicenseHub");
        var copiedCookie = complete.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-LicenseHub=", StringComparison.Ordinal)).Split(';')[0];
        var session = await client.GetFromJsonAsync<AdminSessionView>("/api/v1/admin/auth/session");
        Assert.NotNull(session);
        Assert.True(session.MfaVerified);
        Assert.Equal(fixture.Email, session.Email);
        Assert.Contains("superadmin", session.Capabilities);
        Assert.Empty(session.ApplicationIds);
        // El token anónimo anterior no pertenece a la nueva identidad autenticada.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, "/logout", new { }, csrf)).StatusCode);
        var authenticatedCsrf = await TokenAsync(await client.GetAsync("/api/v1/admin/auth/csrf"));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/logout", new { }, authenticatedCsrf)).StatusCode);
        using var copiedClient = fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), HandleCookies = false });
        copiedClient.DefaultRequestHeaders.Add("Cookie", copiedCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copiedClient.GetAsync("/api/v1/admin/auth/session")).StatusCode);
    }

    [Fact]
    public async Task SerializesMfaConsumptionAndRejectsReuseAndExpiredChallengesAndSessions()
    {
        await using var fixture = await AuthFixture.CreateAsync();
        var first = await fixture.BeginAsync();
        var second = await fixture.BeginAsync();
        var code = SuperAdminBootstrapTests.Totp(fixture.Key);
        async Task<AdminSessionGrant?> CompleteAsync(Guid id)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<IAdminAuthentication>().CompleteAsync(id, new() { Code = code }, "test", default);
            }
            catch (AdminAuthenticationException exception) when (exception.StatusCode == 401) { return null; }
        }
        var results = await Task.WhenAll(CompleteAsync(first.Id), CompleteAsync(second.Id));
        var grant = Assert.Single(results, x => x is not null)!;
        Assert.Null(await CompleteAsync(first.Id));
        Assert.Null(await CompleteAsync(second.Id));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminAuthentication>();
            Assert.NotNull(await service.ValidateAsync(grant.Id, grant.View.UserId, default));
            Assert.Null(await service.ValidateAsync(grant.Id, Guid.NewGuid(), default));
        }
        var expiredChallenge = await fixture.BeginAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<AdminAuthenticationException>(() => scope.ServiceProvider.GetRequiredService<IAdminAuthentication>()
                .CompleteAsync(expiredChallenge.Id, new() { Code = code }, "test", default));
        fixture.Clock.Advance(TimeSpan.FromMinutes(25));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAdminAuthentication>().ValidateAsync(grant.Id, grant.View.UserId, default));
    }

    [Fact]
    public async Task PasswordFailuresLockAccountAndSecurityStampInvalidatesSession()
    {
        await using var fixture = await AuthFixture.CreateAsync();
        var challenge = await fixture.BeginAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAdminAuthentication>();
            var grant = await auth.CompleteAsync(challenge.Id, new() { Code = SuperAdminBootstrapTests.Totp(fixture.Key) }, "test", default);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByEmailAsync(fixture.Email))!;
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
            Assert.Null(await auth.ValidateAsync(grant.Id, user.Id, default));
        }
        for (var i = 0; i < 5; i++)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await Assert.ThrowsAsync<AdminAuthenticationException>(() => scope.ServiceProvider.GetRequiredService<IAdminAuthentication>()
                .BeginAsync(new() { Email = fixture.Email, Password = "invalid" }, "test", default));
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(fixture.Email))!));
        }
        await Assert.ThrowsAsync<AdminAuthenticationException>(() => fixture.BeginAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string suffix, object body, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/auth" + suffix) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<string> TokenAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static void AssertSecureCookie(HttpResponseMessage response, string name)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(name + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }

    internal sealed class AuthFixture : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required NpgsqlConnection Admin { get; init; }
        public required string DatabaseName { get; init; }
        public required string Email { get; init; }
        public required string Password { get; init; }
        public required string Key { get; init; }
        public required TestClock Clock { get; init; }

        public static async Task<AuthFixture> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("LICENSEHUB_TEST_CONNECTION") ?? throw new InvalidOperationException("Requiere PostgreSQL real.");
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            if (parsed.Database?.EndsWith("_tests", StringComparison.Ordinal) != true) throw new InvalidOperationException("Requiere base _tests.");
            var name = "auth_" + Guid.NewGuid().ToString("N") + "_tests";
            var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
            parsed.Database = name;
            var clock = new TestClock();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:LicenseHub", parsed.ConnectionString);
                builder.UseSetting("Jobs:Enabled", "false");
                builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); });
            });
            var fixture = new AuthFixture
            {
                Factory = factory,
                Admin = admin,
                DatabaseName = name,
                Clock = clock,
                Email = Guid.NewGuid().ToString("N") + "@example.invalid",
                Password = "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                Key = SuperAdminBootstrapTests.Base32(RandomNumberGenerator.GetBytes(20))
            };
            try
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<ISuperAdminBootstrap>().InitializeAsync(new()
                {
                    Email = fixture.Email,
                    Password = fixture.Password,
                    AuthenticatorKey = fixture.Key,
                    Code = SuperAdminBootstrapTests.Totp(fixture.Key)
                }, default);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<AdminChallenge> BeginAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAdminAuthentication>().BeginAsync(new() { Email = Email, Password = Password }, "test", default);
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {DatabaseName} WITH (FORCE)", Admin);
            await drop.ExecuteNonQueryAsync();
            await Admin.DisposeAsync();
        }
    }
}
