using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.SecurityTests;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void CrossApplicationForeignKeysIncludeApplicationId()
    {
        using var db = new LicenseHubDbContext(new DbContextOptionsBuilder<LicenseHubDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=metadata_only;Username=test").Options);
        var activation = db.Model.FindEntityType(typeof(LicenseActivationRow))!;
        var licenseLink = Assert.Single(activation.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(LicenseRow));
        Assert.Equal(new[] { "ApplicationId", "LicenseId" }, licenseLink.Properties.Select(x => x.Name));
        var uniqueCanje = Assert.Single(activation.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "LicenseId" }));
        Assert.True(uniqueCanje.IsUnique);
    }

    [Fact]
    public void RenewalsCannotReusePaymentOrOperationId()
    {
        using var db = new LicenseHubDbContext(new DbContextOptionsBuilder<LicenseHubDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=metadata_only;Username=test").Options);
        var renewal = db.Model.FindEntityType(typeof(LicenseRenewalRow))!;
        Assert.Contains(renewal.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(new[] { "PaymentId" }));
        Assert.Contains(renewal.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(new[] { "ApplicationId", "OperationId" }));
    }
}
