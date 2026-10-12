using System.Security.Cryptography;
using System.Text;
using LicenseHub.Application.Integration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Integration;

public sealed class IntegrationAuthentication(LicenseHubDbContext db, TimeProvider clock) : IIntegrationAuthentication
{
    public async Task<IntegrationIdentity?> AuthenticateAsync(string secret, CancellationToken cancellationToken)
    {
        // Reject malformed credentials before accessing storage; do not log the supplied value.
        if (secret.Length != 43 || secret.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
        var now = clock.GetUtcNow();
        var identity = await (from credential in db.Set<ApplicationCredentialRow>().AsNoTracking()
                              join application in db.Set<ApplicationRow>().AsNoTracking() on credential.ApplicationId equals application.Id
                              where credential.SecretHash == hash && credential.RevokedAt == null
                                  && credential.ExpiresAt > now && application.IsActive
                              select new IntegrationIdentity(application.Id, credential.Id, credential.Scopes))
            .SingleOrDefaultAsync(cancellationToken);
        return identity is not null && identity.Scopes.Length > 0 && identity.Scopes.All(IntegrationScopes.IsValid)
            ? identity : null;
    }
}
