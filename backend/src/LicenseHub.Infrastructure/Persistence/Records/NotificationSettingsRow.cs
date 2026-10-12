using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class NotificationSettingsRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required int[] OffsetSeconds { get; set; }
    public required int[] ShortDurationOffsetSeconds { get; set; }
    public bool EmailEnabled { get; set; }
    public bool InAppEnabled { get; set; }
}
