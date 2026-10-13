using System.Globalization;
using System.Text.RegularExpressions;
using LicenseHub.Application.Notifications;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Licensing;

public sealed partial class LicenseOperations : INotificationManagement
{
    public async Task<SettingsResult> SettingsAsync(Guid actor, Guid applicationId, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "notifications:manage", ct);
        var row = await db.Set<NotificationSettingsRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId, ct);
        return row is null ? new(new([604800, 259200, 86400], [60], true, true), Tag(0)) : SettingsView(row);
    }
    public async Task<SettingsResult> SaveSettingsAsync(Guid actor, Guid applicationId, Guid key, string etag, NotificationSettings request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "notifications:manage", ct);
        if (!Offsets(request.OffsetSeconds, 31536000) || !Offsets(request.ShortDurationOffsetSeconds, 86400) || (!request.EmailEnabled && !request.InAppEnabled)) throw Error("validation_failed", 400);
        return await MutateAsync(actor.ToString(), applicationId, key, "notification.settings", new { request, etag }, async () =>
        {
            var binding = "settings:" + applicationId;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({binding}, 0))", ct);
            var row = await db.Set<NotificationSettingsRow>().SingleOrDefaultAsync(x => x.ApplicationId == applicationId, ct);
            Match(row?.Version ?? 0, etag);
            if (row is null)
            {
                row = new() { Id = Guid.NewGuid(), ApplicationId = applicationId, CreatedAt = clock.GetUtcNow(), OffsetSeconds = [], ShortDurationOffsetSeconds = [] };
                db.Add(row);
            }
            row.OffsetSeconds = request.OffsetSeconds; row.ShortDurationOffsetSeconds = request.ShortDurationOffsetSeconds;
            row.EmailEnabled = request.EmailEnabled; row.InAppEnabled = request.InAppEnabled; row.Version++;
            Audit(actor.ToString(), applicationId, row.Id, "notification.settings.updated", trace);
            return SettingsView(row);
        }, null, ct);
    }
    public async Task<TemplatePage> TemplatesAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "notifications:manage", ct);
        var binding = "templates:" + applicationId;
        var offset = Offset(cursor, pageSize, binding);
        var rows = await db.Set<NotificationTemplateRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId).OrderBy(x => x.EventType).ThenBy(x => x.Locale)
            .Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        return new(rows.Take(pageSize).Select(TemplateView).ToArray(), Next(rows.Length, offset, pageSize, binding));
    }
    public async Task<NotificationTemplate> SaveTemplateAsync(Guid actor, Guid applicationId, Guid key, TemplateWrite request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "notifications:manage", ct);
        if (request.EventType is not ("Activated" or "ExpiryApproaching" or "Expired" or "Renewed" or "Suspended" or "Revoked")
            || request.Locale is null || !Regex.IsMatch(request.Locale, "\\A[a-z]{2}(-[A-Z]{2})?\\z")
            || string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 200 || request.Subject.Contains('\r') || request.Subject.Contains('\n')
            || string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 20000 || !Placeholders(request.Subject) || !Placeholders(request.Body)
            || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected < 0) throw Error("validation_failed", 400);
        return await MutateAsync(actor.ToString(), applicationId, key, "notification.template", request, async () =>
        {
            var binding = $"template:{applicationId}:{request.EventType}:{request.Locale}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({binding}, 0))", ct);
            var row = await db.Set<NotificationTemplateRow>().SingleOrDefaultAsync(x => x.ApplicationId == applicationId && x.EventType == request.EventType && x.Locale == request.Locale, ct);
            if ((row?.Version ?? 0) != expected) throw Error("precondition_failed", 412);
            if (row is null)
            {
                row = new() { Id = Guid.NewGuid(), ApplicationId = applicationId, EventType = request.EventType, Locale = request.Locale, Subject = request.Subject, Body = request.Body, CreatedAt = clock.GetUtcNow() };
                db.Add(row);
            }
            row.Subject = request.Subject; row.Body = request.Body; row.Version++;
            Audit(actor.ToString(), applicationId, row.Id, "notification.template.updated", trace);
            return TemplateView(row);
        }, null, ct);
    }
    public async Task<NotificationPage> LogsAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "notifications:read", ct);
        var binding = "notification.logs:" + applicationId;
        var offset = Offset(cursor, pageSize, binding);
        var rows = await db.Set<NotificationLogRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        return new(rows.Take(pageSize).Select(x => new NotificationView(x.Id, x.ApplicationId, x.LicenseId, x.CustomerId, x.EventType,
            x.EntitlementVersion.ToString(CultureInfo.InvariantCulture), x.OffsetSeconds, x.Channel, x.ScheduledAt.UtcDateTime, x.State, x.Attempts, x.LastErrorRedacted, x.CreatedAt.UtcDateTime)).ToArray(),
            Next(rows.Length, offset, pageSize, binding));
    }
    private static bool Offsets(int[] values, int max) => values is not null && values.Length <= 10 && values.All(x => x >= 1 && x <= max) && values.Distinct().Count() == values.Length;
    private static bool Placeholders(string text)
    {
        foreach (var name in new[] { "applicationName", "subjectId", "licenseId", "expiresAt", "eventType" }) text = text.Replace("{" + name + "}", "");
        return !text.Contains('{') && !text.Contains('}');
    }
    private static SettingsResult SettingsView(NotificationSettingsRow row) => new(new(row.OffsetSeconds, row.ShortDurationOffsetSeconds, row.EmailEnabled, row.InAppEnabled), Tag(row.Version));
    private static NotificationTemplate TemplateView(NotificationTemplateRow row) => new(row.EventType, row.Locale, row.Subject, row.Body, row.Version.ToString(CultureInfo.InvariantCulture));
}
