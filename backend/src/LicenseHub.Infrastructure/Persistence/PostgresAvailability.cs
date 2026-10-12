using LicenseHub.Application.Availability;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class PostgresAvailability(LicenseHubDbContext database) : IStorageAvailability
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        await database.Database.CanConnectAsync(cancellationToken)
        && !(await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
}
