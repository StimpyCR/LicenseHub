using LicenseHub.Application.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using LicenseHub.Infrastructure.Persistence;

namespace LicenseHub.IntegrationTests;

public sealed class FeatureManagementTests
{
    [Fact]
    public async Task CatalogIsIsolatedAndSupportsIdempotencyAndVersionedDeactivation()
    {
        await using var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var actor = (await scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>().FindByEmailAsync(fixture.Email))!.Id;
        var service = scope.ServiceProvider.GetRequiredService<IApplicationManagement>();
        var first = await service.CreateAsync(actor, Guid.NewGuid(), new("First", "first", true), "test", default);
        var second = await service.CreateAsync(actor, Guid.NewGuid(), new("Second", "second", true), "test", default);
        var key = Guid.NewGuid();
        var request = new FeatureWrite("reports.export", "Export reports", true);
        var feature = await service.SaveFeatureAsync(actor, first.Id, null, key, null, request, "test", default);
        Assert.Equal(feature, await service.SaveFeatureAsync(actor, first.Id, null, key, null, request, "test", default));
        Assert.Empty((await service.FeaturesAsync(actor, second.Id, null, 25, default)).Items);
        var disabled = await service.SaveFeatureAsync(actor, first.Id, feature.Code, Guid.NewGuid(), feature.Etag, request with { IsActive = false }, "test", default);
        Assert.False(disabled.IsActive);
        Assert.Equal("\"2\"", disabled.Etag);
        var stale = await Assert.ThrowsAsync<ManagementException>(() => service.SaveFeatureAsync(actor, first.Id, feature.Code, Guid.NewGuid(), feature.Etag, request, "test", default));
        Assert.Equal(412, stale.Status);
        var denied = await Assert.ThrowsAsync<ManagementException>(() => service.FeaturesAsync(Guid.NewGuid(), first.Id, null, 25, default));
        Assert.Equal(404, denied.Status);
        var mismatch = await Assert.ThrowsAsync<ManagementException>(() => service.SaveFeatureAsync(actor, first.Id, feature.Code, Guid.NewGuid(), disabled.Etag, request with { Code = "changed" }, "test", default));
        Assert.Equal(400, mismatch.Status);
    }
}
