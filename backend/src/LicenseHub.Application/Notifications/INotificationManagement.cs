using System.Text.Json.Serialization;

namespace LicenseHub.Application.Notifications;

public interface INotificationManagement
{
    Task<SettingsResult> SettingsAsync(Guid actor, Guid applicationId, CancellationToken ct);
    Task<SettingsResult> SaveSettingsAsync(Guid actor, Guid applicationId, Guid key, string etag, NotificationSettings request, string trace, CancellationToken ct);
    Task<TemplatePage> TemplatesAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct);
    Task<NotificationTemplate> SaveTemplateAsync(Guid actor, Guid applicationId, Guid key, TemplateWrite request, string trace, CancellationToken ct);
    Task<NotificationPage> LogsAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct);
}
public sealed record NotificationSettings([property: JsonRequired] int[] OffsetSeconds, [property: JsonRequired] int[] ShortDurationOffsetSeconds,
    [property: JsonRequired] bool EmailEnabled, [property: JsonRequired] bool InAppEnabled);
public sealed record SettingsResult(NotificationSettings Value, string Etag);
public sealed record NotificationTemplate(string EventType, string Locale, string Subject, string Body, string Version);
public sealed record TemplatePage(NotificationTemplate[] Items, string? NextCursor);
public sealed record TemplateWrite([property: JsonRequired] string EventType, [property: JsonRequired] string Locale,
    [property: JsonRequired] string Subject, [property: JsonRequired] string Body, [property: JsonRequired] string ExpectedVersion);
public sealed record NotificationView(Guid Id, Guid ApplicationId, Guid LicenseId, Guid CustomerId, string EventType,
    string EntitlementVersion, int OffsetSeconds, string Channel, DateTime ScheduledAt, string State, int Attempts, string? LastError, DateTime CreatedAt);
public sealed record NotificationPage(NotificationView[] Items, string? NextCursor);
