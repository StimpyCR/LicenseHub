using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LicenseHub.Api.Administration;
using LicenseHub.Api.Integration;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Integration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class IntegrationAuthenticationTests
{
    [Fact]
    public async Task RejectsExpiredRevokedInactiveAndUnknownCredentialsWithoutCaching()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var (app, credential, secret) = await SeedAsync(db, fixture.Clock.GetUtcNow());
        var service = scope.ServiceProvider.GetRequiredService<IIntegrationAuthentication>();
        Assert.Null(await service.AuthenticateAsync("invalid", default));
        Assert.Null(await service.AuthenticateAsync(NewSecret(), default));
        var identity = await service.AuthenticateAsync(secret, default);
        Assert.NotNull(identity);
        Assert.Equal(app.Id, identity.ApplicationId);
        Assert.Equal(credential.Id, identity.CredentialId);
        credential.RevokedAt = fixture.Clock.GetUtcNow();
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(secret, default));
        credential.RevokedAt = null;
        app.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(secret, default));
        app.IsActive = true;
        credential.ExpiresAt = fixture.Clock.GetUtcNow();
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(secret, default));
        credential.ExpiresAt = fixture.Clock.GetUtcNow().AddHours(1);
        credential.Scopes = ["unrecognized"];
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(secret, default));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AuthenticateAsync(secret, cancelled.Token));
    }

    [Fact]
    public async Task SchemeResolvesApplicationAndEnforcesScopesWithoutAcceptingAdminIdentity()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var (app, _, secret) = await SeedAsync(db, fixture.Clock.GetUtcNow());
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Headers.Authorization = "Bearer " + secret;
        context.Request.QueryString = new("?applicationId=" + Guid.NewGuid());
        var result = await context.AuthenticateAsync(IntegrationAuthenticationHandler.SchemeName);
        Assert.True(result.Succeeded);
        Assert.Equal(app.Id.ToString(), result.Principal!.FindFirstValue(IntegrationAuthenticationHandler.ApplicationClaim));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(result.Principal, null, IntegrationScopes.Read)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(result.Principal, null, IntegrationScopes.Activate)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(result.Principal, null, "AdminSession")).Succeeded);
        await using var missingScope = fixture.Factory.Services.CreateAsyncScope();
        var missingContext = new DefaultHttpContext { RequestServices = missingScope.ServiceProvider };
        missingContext.Request.QueryString = new("?access_token=" + secret);
        Assert.True((await missingContext.AuthenticateAsync(IntegrationAuthenticationHandler.SchemeName)).None);
        await using var challengeScope = fixture.Factory.Services.CreateAsyncScope();
        var challenge = new DefaultHttpContext { RequestServices = challengeScope.ServiceProvider };
        challenge.Response.Body = new MemoryStream();
        await challenge.ChallengeAsync(IntegrationAuthenticationHandler.SchemeName);
        Assert.Equal(401, challenge.Response.StatusCode);
        Assert.Equal("Bearer", challenge.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task AdministrativeCapabilitiesRemainPairedWithTheirApplication()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var (appA, _, _) = await SeedAsync(db, fixture.Clock.GetUtcNow());
        var (appB, _, _) = await SeedAsync(db, fixture.Clock.GetUtcNow());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = new AdminUser { Id = Guid.NewGuid(), UserName = "paired@example.invalid", Email = "paired@example.invalid", TwoFactorEnabled = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        db.AddRange(new ApplicationUserGrantRow { UserId = user.Id, ApplicationId = appA.Id, Capability = "licenses:read" },
            new ApplicationUserGrantRow { UserId = user.Id, ApplicationId = appB.Id, Capability = "licenses:manage" });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("licensehub:mfa", "verified")
        ], IdentityConstants.ApplicationScheme));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        async Task<bool> AllowedAsync(Guid app, string capability) => (await authorization.AuthorizeAsync(
            principal, app, new[] { new ApplicationCapabilityRequirement(capability) })).Succeeded;
        Assert.True(await AllowedAsync(appA.Id, "licenses:read"));
        Assert.True(await AllowedAsync(appB.Id, "licenses:manage"));
        Assert.False(await AllowedAsync(appA.Id, "licenses:manage"));
        Assert.False(await AllowedAsync(appB.Id, "licenses:read"));
        Assert.False(await AllowedAsync(Guid.NewGuid(), "licenses:read"));
        db.RemoveRange(await db.Set<ApplicationUserGrantRow>().Where(x => x.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        Assert.False(await AllowedAsync(appA.Id, "licenses:read"));
        var administrator = await users.FindByEmailAsync(fixture.Email);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IApplicationAccess>()
            .IsAllowedAsync(administrator!.Id, appA.Id, "licenses:manage", default));
    }

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<(ApplicationRow App, ApplicationCredentialRow Credential, string Secret)> SeedAsync(LicenseHubDbContext db, DateTimeOffset now)
    {
        var app = new ApplicationRow { Id = Guid.NewGuid(), Slug = Guid.NewGuid().ToString("N"), Name = "Isolated auth test", IsActive = true, CreatedAt = now };
        var secret = NewSecret();
        var credential = new ApplicationCredentialRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1),
            SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret))),
            Scopes = [IntegrationScopes.Read]
        };
        db.AddRange(app, credential);
        await db.SaveChangesAsync();
        return (app, credential, secret);
    }
}
