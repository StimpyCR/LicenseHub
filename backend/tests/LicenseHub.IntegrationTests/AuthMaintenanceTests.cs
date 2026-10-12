using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class AuthMaintenanceTests
{
    [Fact]
    public async Task CleanupDeletesOnlyExpiredRecordsAndCanBeRepeated()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>().FindByEmailAsync(fixture.Email))!;
        var now = fixture.Clock.GetUtcNow();
        db.AddRange(
            new AdminChallengeRow { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = now.AddDays(-2) },
            new AdminChallengeRow { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = now.AddMinutes(5) },
            new AdminSessionRow { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = now.AddDays(-31) },
            new AdminSessionRow { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = now.AddMinutes(30) },
            new AdminMfaUseRow { UserId = user.Id, TokenHash = new string('A', 64), ExpiresAt = now.AddMinutes(-1) },
            new AdminMfaUseRow { UserId = user.Id, TokenHash = new string('B', 64), ExpiresAt = now.AddMinutes(3) });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAuthMaintenance>();
        await service.CleanupAsync(default);
        await service.CleanupAsync(default);
        Assert.Equal(1, await db.Set<AdminChallengeRow>().CountAsync());
        Assert.Equal(1, await db.Set<AdminSessionRow>().CountAsync());
        Assert.Equal(1, await db.Set<AdminMfaUseRow>().CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.True(await db.Set<AuditLogRow>().AnyAsync(x => x.Action == "admin.bootstrap"));
    }
}
