using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class AdminUserManagementTests
{
    [Fact]
    public async Task CreationIsConcurrentIdempotentAndCannotGrantSuperAdmin()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var actor = await RootAsync(fixture);
        var key = Guid.NewGuid();
        var request = new AdminUserCreate("delegate@example.invalid");
        Task<AdminUserProfile> CreateAsync() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().CreateAsync(actor, key, request, "test", default));
        var results = await Task.WhenAll(CreateAsync(), CreateAsync(), CreateAsync());
        var id = results[0].Id;
        Assert.Single(results.Select(x => x.Id).Distinct());
        Assert.All(results, profile => { Assert.False(profile.IsSuperAdmin); Assert.False(profile.MfaEnrolled); Assert.Empty(profile.Grants); });
        var conflict = await Assert.ThrowsAsync<ManagementException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().CreateAsync(actor, key, new("changed@example.invalid"), "test", default)));
        Assert.Equal(409, conflict.Status);
        var escalation = await Assert.ThrowsAsync<ManagementException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, id, results[0].Etag,
            new(false, [new(Guid.NewGuid(), "superadmin")], "Invalid privilege"), "test", default)));
        Assert.Equal(400, escalation.Status);
        var rootChange = await Assert.ThrowsAsync<ManagementException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, actor, "\"0\"", new(true, [], "Disable root"), "test", default)));
        Assert.Equal(403, rootChange.Status);
        await Assert.ThrowsAsync<AdminAuthenticationException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminAuthentication>().BeginAsync(new() { Email = request.Email, Password = "unknown" }, "test", default)));
    }

    [Fact]
    public async Task EnrollmentAndScopedGrantsWorkAndSuspensionRevokesSessions()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var actor = await RootAsync(fixture);
        var app = await ScopedAsync(fixture, services => services.GetRequiredService<IApplicationManagement>().CreateAsync(actor, Guid.NewGuid(), new("Example", "example", true), "test", default));
        var pending = await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().CreateAsync(actor, Guid.NewGuid(), new("delegate@example.invalid"), "test", default));
        var withGrants = await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, pending.Id, pending.Etag,
            new(false, [new(app.Id, "applications:read"), new(app.Id, "features:manage")], "Assign minimum permissions"), "test", default));
        var key = SuperAdminBootstrapTests.Base32(RandomNumberGenerator.GetBytes(20));
        var password = "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAdminEnrollment>().EnrollAsync(new()
            {
                UserId = pending.Id,
                Email = pending.Email,
                Confirmation = "ENROLL:" + pending.Id,
                Password = password,
                AuthenticatorKey = key,
                Code = SuperAdminBootstrapTests.Totp(key),
                Reason = "Owner enrollment test"
            }, "test-operator", default);
        var profile = (await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().ListAsync(actor, null, 25, default))).Items.Single(x => x.Id == pending.Id);
        Assert.True(profile.MfaEnrolled);
        var challenge = await ScopedAsync(fixture, services => services.GetRequiredService<IAdminAuthentication>().BeginAsync(new() { Email = pending.Email, Password = password }, "test", default));
        var grant = await ScopedAsync(fixture, services => services.GetRequiredService<IAdminAuthentication>().CompleteAsync(challenge.Id, new() { Code = SuperAdminBootstrapTests.Totp(key) }, "test", default));
        Assert.DoesNotContain("superadmin", grant.View.Capabilities);
        Assert.True(await ScopedAsync(fixture, services => services.GetRequiredService<IApplicationAccess>().IsAllowedAsync(pending.Id, app.Id, "features:manage", default)));
        Assert.False(await ScopedAsync(fixture, services => services.GetRequiredService<IApplicationAccess>().IsAllowedAsync(pending.Id, Guid.NewGuid(), "features:manage", default)));
        var forbidden = await Assert.ThrowsAsync<ManagementException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().ListAsync(pending.Id, null, 25, default)));
        Assert.Equal(403, forbidden.Status);
        var suspended = await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, pending.Id, profile.Etag,
            new(true, profile.Grants, "Operator suspension"), "test", default));
        Assert.True(suspended.IsDisabled);
        Assert.Null(await ScopedAsync(fixture, services => services.GetRequiredService<IAdminAuthentication>().ValidateAsync(grant.Id, pending.Id, default)));
        Assert.False(await ScopedAsync(fixture, services => services.GetRequiredService<IApplicationAccess>().IsAllowedAsync(pending.Id, app.Id, "features:manage", default)));
        await Assert.ThrowsAsync<AdminAuthenticationException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminAuthentication>().BeginAsync(new() { Email = pending.Email, Password = password }, "test", default)));
        var stale = await Assert.ThrowsAsync<ManagementException>(() => ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, pending.Id, profile.Etag,
            new(false, [], "Stale update"), "test", default)));
        Assert.Equal(412, stale.Status);
        var recoveryKey = SuperAdminBootstrapTests.Base32(RandomNumberGenerator.GetBytes(20));
        var replacementPassword = "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var recoveryRequest = new AdminRecoveryRequest
        {
            UserId = pending.Id,
            Email = pending.Email,
            Confirmation = "RECOVER:" + pending.Id,
            Password = replacementPassword,
            AuthenticatorKey = recoveryKey,
            Code = SuperAdminBootstrapTests.Totp(recoveryKey),
            Reason = "Delegate authenticator replacement"
        };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAdminRecovery>().RecoverAsync(recoveryRequest, "test-operator", default));
        await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().UpdateAsync(actor, pending.Id, suspended.Etag,
            new(false, suspended.Grants, "Explicit reactivation before recovery"), "test", default));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAdminRecovery>().RecoverAsync(recoveryRequest, "test-operator", default);
        var recovered = (await ScopedAsync(fixture, services => services.GetRequiredService<IAdminUsers>().ListAsync(actor, null, 25, default))).Items.Single(x => x.Id == pending.Id);
        Assert.False(recovered.IsSuperAdmin);
        Assert.Equal(profile.Grants, recovered.Grants);
    }

    [Fact]
    public async Task HttpRequiresSessionCsrfAndRejectsRoleInjection()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = fixture.Email, password = fixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(fixture.Key) })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/users", new AdminUserCreate("delegate@example.invalid"))).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/users", new { email = "delegate@example.invalid", role = "SuperAdmin" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", new AdminUserCreate("delegate@example.invalid"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<AdminUserProfile>())!;
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/api/v1/admin/users/" + user.Id, new AdminUserAccessWrite(true, [], "Suspend pending account"))).StatusCode);
    }

    private static Task<Guid> RootAsync(AdminAuthenticationTests.AuthFixture fixture) => ScopedAsync(fixture, async services =>
        (await services.GetRequiredService<UserManager<AdminUser>>().FindByEmailAsync(fixture.Email))!.Id);
    private static async Task<T> ScopedAsync<T>(AdminAuthenticationTests.AuthFixture fixture, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}
