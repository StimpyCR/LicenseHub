using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class NotificationLogRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public Guid LicenseId { get; set; }
    public Guid CustomerId { get; set; }
    public required string EventType { get; set; }
    public long EntitlementVersion { get; set; }
    public int OffsetSeconds { get; set; }
    public required string Channel { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public required string State { get; set; }
    public int Attempts { get; set; }
    public string? ProviderId { get; set; }
    public string? LastErrorRedacted { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}
