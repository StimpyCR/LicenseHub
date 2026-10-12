using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class NotificationTemplateRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string EventType { get; set; }
    public required string Locale { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
}
