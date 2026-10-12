using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace LicenseHub.IntegrationTests;

public sealed class AdminRecoveryTests
{
    [Fact]
    public async Task RecoveryRequiresNewVerifiedAuthenticatorAndRollsBackFailure()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var userId = await UserIdAsync(fixture);
        var key = SuperAdminBootstrapTests.Base32(RandomNumberGenerator.GetBytes(20));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminRecovery>();
            var invalid = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecoverAsync(
                Request(fixture, userId, key, "invalid"), "test-operator", default));
            Assert.Equal("recovery_invalid_input", invalid.Message);
        }
        // Well-formed but wrong TOTP exercises rollback after replacing the stored key.
        var validCode = SuperAdminBootstrapTests.Totp(key);
        var wrong = ((int.Parse(validCode) + 1) % 1000000).ToString("D6");
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAdminRecovery>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecoverAsync(Request(fixture, userId, key, wrong), "test-operator", default));
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            Assert.Equal(fixture.Key, await users.GetAuthenticatorKeyAsync(user));
            Assert.True(await users.CheckPasswordAsync(user, fixture.Password));
        }
    }

    [Fact]
    public async Task RecoveryInvalidatesSessionsChallengesAndPasswordAndAuditsOnce()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var userId = await UserIdAsync(fixture);
        var challenge = await fixture.BeginAsync();
        AdminSessionGrant grant;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            grant = await scope.ServiceProvider.GetRequiredService<IAdminAuthentication>().CompleteAsync(challenge.Id,
                new() { Code = SuperAdminBootstrapTests.Totp(fixture.Key) }, "test", default);
        var pending = await fixture.BeginAsync();
        var key = SuperAdminBootstrapTests.Base32(RandomNumberGenerator.GetBytes(20));
        var request = Request(fixture, userId, key, SuperAdminBootstrapTests.Totp(key));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAdminRecovery>().RecoverAsync(request, "test-operator", default);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAdminAuthentication>();
            Assert.Null(await auth.ValidateAsync(grant.Id, userId, default));
            await Assert.ThrowsAsync<AdminAuthenticationException>(() => auth.CompleteAsync(pending.Id,
                new() { Code = SuperAdminBootstrapTests.Totp(key) }, "test", default));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            Assert.False(await users.CheckPasswordAsync(user, fixture.Password));
            Assert.True(await users.CheckPasswordAsync(user, request.Password));
            Assert.True(user.TwoFactorEnabled);
            var audit = await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Set<AuditLogRow>()
                .SingleAsync(x => x.Action == "admin.recovered");
            Assert.Equal("test-operator", audit.ActorId);
            Assert.Equal(userId, audit.EntityId);
        }
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAdminRecovery>()
                .RecoverAsync(request, "test-operator", default));
            Assert.Equal("recovery_new_key_required", repeated.Message);
        }
    }

    private static AdminRecoveryRequest Request(AdminAuthenticationTests.AuthFixture fixture, Guid id, string key, string code) => new()
    {
        UserId = id,
        Email = fixture.Email,
        Confirmation = "RECOVER:" + id,
        Reason = "Lost authenticator test",
        Password = "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        AuthenticatorKey = key,
        Code = code
    };
    private static async Task<Guid> UserIdAsync(AdminAuthenticationTests.AuthFixture fixture)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>().FindByEmailAsync(fixture.Email))!.Id;
    }
}
