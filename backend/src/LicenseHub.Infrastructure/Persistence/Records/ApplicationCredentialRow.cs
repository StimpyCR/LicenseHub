using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class ApplicationCredentialRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string SecretHash { get; set; }
    public required string[] Scopes { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
