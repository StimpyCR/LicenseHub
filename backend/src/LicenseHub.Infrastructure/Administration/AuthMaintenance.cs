using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

public sealed class AuthMaintenance(LicenseHubDbContext db, TimeProvider clock) : IAuthMaintenance
{
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var challengeCutoff = now.AddDays(-1);
        var sessionCutoff = now.AddDays(-30);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724194632);", cancellationToken);
        await db.Set<AdminMfaUseRow>().Where(x => x.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
        await db.Set<AdminChallengeRow>().Where(x => x.ExpiresAt < challengeCutoff).ExecuteDeleteAsync(cancellationToken);
        await db.Set<AdminSessionRow>().Where(x => x.ExpiresAt < sessionCutoff).ExecuteDeleteAsync(cancellationToken);
        // Identity, audit history and live sessions/challenges are never removed here.
        await transaction.CommitAsync(cancellationToken);
    }
}
