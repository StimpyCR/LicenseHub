using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class LicenseRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public Guid? CustomerId { get; set; }
    public required string KeyHash { get; set; }
    public LicenseState State { get; set; }
    public required string StartMode { get; set; }
    public string? DurationUnit { get; set; }
    public int? DurationValue { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsLifetime { get; set; }
    public long EntitlementVersion { get; set; }
}
