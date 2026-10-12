using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LicenseHubDbContext>
{
    public LicenseHubDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__LicenseHub")
            ?? throw new InvalidOperationException("Configura ConnectionStrings__LicenseHub mediante entorno o secret manager.");
        return new LicenseHubDbContext(new DbContextOptionsBuilder<LicenseHubDbContext>().UseNpgsql(connection).Options);
    }
}
