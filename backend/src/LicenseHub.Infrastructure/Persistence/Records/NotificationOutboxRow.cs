using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class NotificationOutboxRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string EventType { get; set; }
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Attempts { get; set; }
}
