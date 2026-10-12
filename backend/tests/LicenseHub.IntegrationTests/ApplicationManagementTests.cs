using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Integration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class ApplicationManagementTests
{
    [Fact]
    public async Task CreatesOnceUnderConcurrentRetriesAndRejectsChangedPayloadAndStaleUpdates()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var grant = await LoginAsync(fixture);
        var key = Guid.NewGuid();
        var request = new ApplicationWrite("StockMini", "stockmini", true);
        async Task<ApplicationView> CreateAsync()
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IApplicationManagement>().CreateAsync(grant.View.UserId, key, request, "test", default);
        }
        var results = await Task.WhenAll(CreateAsync(), CreateAsync(), CreateAsync());
        Assert.Single(results.Select(x => x.Id).Distinct());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IApplicationManagement>();
        var conflict = await Assert.ThrowsAsync<ManagementException>(() => service.CreateAsync(grant.View.UserId, key, request with { Name = "changed" }, "test", default));
        Assert.Equal("idempotency_conflict", conflict.Code);
        var updated = await service.UpdateAsync(grant.View.UserId, results[0].Id, Guid.NewGuid(), results[0].Etag, request with { IsActive = false }, "test", default);
        Assert.False(updated.IsActive);
        Assert.Equal("\"2\"", updated.Etag);
        var stale = await Assert.ThrowsAsync<ManagementException>(() => service.UpdateAsync(grant.View.UserId, updated.Id, Guid.NewGuid(), results[0].Etag, request, "test", default));
        Assert.Equal(412, stale.Status);
        Assert.Single((await service.ListAsync(grant.View.UserId, null, 25, default)).Items);
    }

    [Fact]
    public async Task CredentialSecretIsNeverStoredOrReplayedAndRevocationInvalidatesAuthentication()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        var grant = await LoginAsync(fixture);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IApplicationManagement>();
        var app = await service.CreateAsync(grant.View.UserId, Guid.NewGuid(), new("StockMini", "stockmini", true), "test", default);
        var key = Guid.NewGuid();
        var request = new CredentialWrite([IntegrationScopes.Read], fixture.Clock.GetUtcNow().AddHours(1));
        var issued = await service.IssueAsync(grant.View.UserId, app.Id, key, request, "test", default);
        Assert.Equal(43, issued.Secret.Length);
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var record = await db.Set<ApplicationCredentialRow>().FindAsync(issued.Credential.Id);
        Assert.NotEqual(issued.Secret, record!.SecretHash);
        Assert.Null(Assert.Single(db.Set<IdempotencyRow>().Local, x => x.Key == key).ResponseJson);
        var retry = await Assert.ThrowsAsync<ManagementException>(() => service.IssueAsync(grant.View.UserId, app.Id, key, request, "test", default));
        Assert.Equal("secret_not_recoverable", retry.Code);
        var page = await service.CredentialsAsync(grant.View.UserId, app.Id, null, 25, default);
        Assert.DoesNotContain(issued.Secret, JsonSerializer.Serialize(page));
        var auth = scope.ServiceProvider.GetRequiredService<IIntegrationAuthentication>();
        Assert.NotNull(await auth.AuthenticateAsync(issued.Secret, default));
        var revoked = await service.RevokeAsync(grant.View.UserId, issued.Credential.Id, Guid.NewGuid(), issued.Credential.Etag, new("Rotation"), "test", default);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Null(await auth.AuthenticateAsync(issued.Secret, default));
        var deniedRevoke = await Assert.ThrowsAsync<ManagementException>(() => service.RevokeAsync(Guid.NewGuid(),
            issued.Credential.Id, Guid.NewGuid(), revoked.Etag, new("Unauthorized"), "test", default));
        Assert.Equal(404, deniedRevoke.Status);
        Assert.Empty((await service.ListAsync(Guid.NewGuid(), null, 25, default)).Items);
        var denied = await Assert.ThrowsAsync<ManagementException>(() => service.CredentialsAsync(Guid.NewGuid(), app.Id, null, 25, default));
        Assert.Equal(404, denied.Status);
    }

    [Fact]
    public async Task HttpEnforcesMfaCsrfIdempotencyAndRequiredFields()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/applications")).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = fixture.Email, password = fixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(fixture.Key) })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var request = new ApplicationWrite("StockMini", "stockmini", true);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/applications", request)).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/applications", request)).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/applications", new { name = "Missing fields" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/admin/applications", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var app = await response.Content.ReadFromJsonAsync<ApplicationView>();
        Assert.Equal(app!.Etag, response.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/api/v1/admin/applications/" + app.Id, request)).StatusCode);
    }

    private static async Task<AdminSessionGrant> LoginAsync(AdminAuthenticationTests.AuthFixture fixture)
    {
        var challenge = await fixture.BeginAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAdminAuthentication>().CompleteAsync(challenge.Id,
            new() { Code = SuperAdminBootstrapTests.Totp(fixture.Key) }, "test", default);
    }
}
