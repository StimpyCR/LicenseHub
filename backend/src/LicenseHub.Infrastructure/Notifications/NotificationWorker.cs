using System.Globalization;
using System.Text.RegularExpressions;
using LicenseHub.Application.Licensing;
using LicenseHub.Application.Notifications;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Notifications;

public sealed class NotificationWorker(LicenseHubDbContext db, TimeProvider clock, IEmailSender sender,
    ICursorProtection cursors, string locale = "es") : INotificationWorker, IWarningQueries
{
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = await db.Set<LicenseRow>().AsNoTracking().Where(x => !x.IsLifetime && x.ExpiresAt <= now
            && (x.State == LicenseState.Active || x.State == LicenseState.Pending)).OrderBy(x => x.ExpiresAt).Take(100).ToArrayAsync(ct);
        foreach (var item in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var row = await db.Set<LicenseRow>().FromSqlInterpolated($"SELECT * FROM \"Licenses\" WHERE \"Id\" = {item.Id} FOR UPDATE").SingleAsync(ct);
            if (row.State is LicenseState.Active or LicenseState.Pending && !row.IsLifetime && row.ExpiresAt <= clock.GetUtcNow())
            {
                // Expiration was already encoded in the entitlement's expiry; keep its generation to deduplicate the scheduled expiry notice.
                row.State = LicenseState.Expired; row.Version++;
                db.Add(new AuditLogRow
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = row.ApplicationId,
                    EntityId = row.Id,
                    ActorId = "system:expiry",
                    Action = "license.expired",
                    CorrelationId = "notification-reconcile",
                    CreatedAt = clock.GetUtcNow()
                });
                if (row.CustomerId is not null) db.Add(new NotificationOutboxRow
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = row.ApplicationId,
                    AggregateId = row.Id,
                    AggregateVersion = row.EntitlementVersion,
                    EventType = "Expired",
                    PayloadJson = "{}",
                    CreatedAt = clock.GetUtcNow(),
                    Version = 1
                });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
        for (var i = 0; i < 100; i++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var entry = await db.Set<NotificationOutboxRow>().FromSqlRaw("SELECT * FROM \"NotificationOutbox\" WHERE \"ProcessedAt\" IS NULL ORDER BY \"CreatedAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
            if (entry is null) break;
            var license = await db.Set<LicenseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == entry.ApplicationId && x.Id == entry.AggregateId, ct);
            if (license is not null && license.CustomerId is not null && license.EntitlementVersion == entry.AggregateVersion && license.State != LicenseState.Cancelled)
            {
                var settings = await db.Set<NotificationSettingsRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == entry.ApplicationId, ct);
                var channels = new List<string>();
                if (settings?.InAppEnabled ?? true) channels.Add("InApp");
                if (settings?.EmailEnabled ?? true) channels.Add("Email");
                var effective = LicenseValidity.EffectiveState(license.State, license.IsLifetime, license.ExpiresAt, clock.GetUtcNow());
                if (entry.EventType != "Schedule" && (entry.EventType is not ("Activated" or "Renewed") || effective == LicenseState.Active))
                    foreach (var channel in channels) await PlanAsync(license, entry.EventType, 0, clock.GetUtcNow(), channel, ct);
                if (effective is LicenseState.Active or LicenseState.Pending && !license.IsLifetime && license.ExpiresAt is not null)
                {
                    var offsets = (license.ExpiresAt.Value - clock.GetUtcNow()).TotalSeconds < 86400
                        ? settings?.ShortDurationOffsetSeconds ?? [60] : settings?.OffsetSeconds ?? [604800, 259200, 86400];
                    foreach (var offset in offsets)
                    {
                        var schedule = license.ExpiresAt.Value.AddSeconds(-offset);
                        if (schedule < clock.GetUtcNow().AddMinutes(-2)) continue;
                        foreach (var channel in channels) await PlanAsync(license, "ExpiryApproaching", offset, schedule, channel, ct);
                    }
                    foreach (var channel in channels) await PlanAsync(license, "Expired", 0, license.ExpiresAt.Value, channel, ct);
                }
            }
            entry.ProcessedAt = clock.GetUtcNow(); entry.Attempts++; entry.Version++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
    }

    public async Task DispatchAsync(CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            var log = await db.Set<NotificationLogRow>().FromSqlInterpolated($"SELECT * FROM \"NotificationLogs\" WHERE \"State\" IN ('Pending','Failed') AND \"Attempts\" < 5 AND \"ScheduledAt\" <= {now} AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" <= {now}) ORDER BY \"ScheduledAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
            if (log is null) break;
            var license = await db.Set<LicenseRow>().AsNoTracking().SingleAsync(x => x.ApplicationId == log.ApplicationId && x.Id == log.LicenseId, ct);
            var settings = await db.Set<NotificationSettingsRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == log.ApplicationId, ct);
            var disabled = log.Channel == "Email" ? !(settings?.EmailEnabled ?? true) : !(settings?.InAppEnabled ?? true);
            if (license.EntitlementVersion != log.EntitlementVersion || license.State == LicenseState.Cancelled || disabled)
            {
                log.State = "Cancelled";
                log.LastErrorRedacted = disabled ? "channel_disabled" : "obsolete_entitlement";
            }
            else
            {
                log.Attempts++;
                try
                {
                    if (log.Channel == "Email")
                    {
                        var customer = await db.Set<CustomerRow>().AsNoTracking().SingleAsync(x => x.ApplicationId == log.ApplicationId && x.Id == log.CustomerId, ct);
                        if (customer.Email is null) log.State = "Skipped";
                        else
                        {
                            var content = await ContentAsync(log, license, customer, ct);
                            log.ProviderId = await sender.SendAsync(new(log.Id, customer.Email, content.Subject, content.Body), ct);
                            log.State = "Sent";
                        }
                    }
                    else log.State = "Sent";
                    log.LastErrorRedacted = null; log.LeaseUntil = null;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    log.State = "Failed";
                    log.LastErrorRedacted = error is EmailDeliveryException { Code: "mail_not_configured" or "delivery_timeout" } delivery ? delivery.Code : "delivery_failed";
                    log.LeaseUntil = clock.GetUtcNow().AddMinutes(Math.Pow(2, log.Attempts - 1));
                }
            }
            log.Version++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
    }

    public async Task<WarningPage> ListAsync(Guid applicationId, string subjectId, string? cursor, int pageSize, CancellationToken ct)
    {
        if (subjectId is null || !Regex.IsMatch(subjectId, "\\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\\z") || pageSize is < 1 or > 100)
            throw new LicenseHub.Application.Administration.ManagementException("validation_failed", 400);
        var binding = $"warnings:{applicationId}:{subjectId}";
        var offset = 0;
        try
        {
            if (cursor is not null)
            {
                var value = cursors.Unprotect(cursor).Split('|');
                if (value.Length != 2 || value[0] != binding || !int.TryParse(value[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset is < 0 or > 1000000)
                    throw new System.Security.Cryptography.CryptographicException();
            }
        }
        catch (System.Security.Cryptography.CryptographicException) { throw new LicenseHub.Application.Administration.ManagementException("validation_failed", 400); }
        var now = clock.GetUtcNow();
        var rows = await (from log in db.Set<NotificationLogRow>().AsNoTracking()
                          join customer in db.Set<CustomerRow>() on new { log.ApplicationId, Id = log.CustomerId } equals new { customer.ApplicationId, customer.Id }
                          join license in db.Set<LicenseRow>() on new { log.ApplicationId, Id = log.LicenseId } equals new { license.ApplicationId, license.Id }
                          where log.ApplicationId == applicationId && customer.SubjectId == subjectId && log.Channel == "InApp"
                              && log.State == "Sent" && log.ScheduledAt <= now && log.EntitlementVersion == license.EntitlementVersion
                          orderby log.CreatedAt, log.Id
                          select log).Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        var items = new List<WarningView>();
        foreach (var log in rows.Take(pageSize))
        {
            var license = await db.Set<LicenseRow>().AsNoTracking().SingleAsync(x => x.ApplicationId == applicationId && x.Id == log.LicenseId, ct);
            var customer = await db.Set<CustomerRow>().AsNoTracking().SingleAsync(x => x.ApplicationId == applicationId && x.Id == log.CustomerId, ct);
            var content = await ContentAsync(log, license, customer, ct);
            items.Add(new(log.Id, Code(log.EventType), log.EventType is "Expired" or "Revoked" ? "Error" : log.EventType == "ExpiryApproaching" ? "Warning" : "Info",
                content.Body[..Math.Min(1000, content.Body.Length)], log.CreatedAt.UtcDateTime, log.LicenseId));
        }
        return new(items.ToArray(), rows.Length > pageSize ? cursors.Protect(binding + "|" + (offset + pageSize).ToString(CultureInfo.InvariantCulture)) : null);
    }

    private async Task PlanAsync(LicenseRow license, string eventType, int offset, DateTimeOffset schedule, string channel, CancellationToken ct)
    {
        if (await db.Set<NotificationLogRow>().AnyAsync(x => x.LicenseId == license.Id && x.EntitlementVersion == license.EntitlementVersion && x.EventType == eventType && x.OffsetSeconds == offset && x.Channel == channel, ct)
            || db.Set<NotificationLogRow>().Local.Any(x => x.LicenseId == license.Id && x.EntitlementVersion == license.EntitlementVersion && x.EventType == eventType && x.OffsetSeconds == offset && x.Channel == channel)) return;
        db.Add(new NotificationLogRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = license.ApplicationId,
            LicenseId = license.Id,
            CustomerId = license.CustomerId!.Value,
            EntitlementVersion = license.EntitlementVersion,
            EventType = eventType,
            OffsetSeconds = offset,
            Channel = channel,
            State = "Pending",
            ScheduledAt = schedule,
            CreatedAt = clock.GetUtcNow(),
            Version = 1
        });
    }
    private async Task<(string Subject, string Body)> ContentAsync(NotificationLogRow log, LicenseRow license, CustomerRow customer, CancellationToken ct)
    {
        var template = await db.Set<NotificationTemplateRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == log.ApplicationId && x.EventType == log.EventType && x.Locale == locale, ct);
        var application = await db.Set<ApplicationRow>().AsNoTracking().SingleAsync(x => x.Id == log.ApplicationId, ct);
        string Render(string text) => text.Replace("{applicationName}", application.Name).Replace("{subjectId}", customer.SubjectId)
            .Replace("{licenseId}", license.Id.ToString()).Replace("{expiresAt}", license.ExpiresAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "Vitalicia").Replace("{eventType}", log.EventType);
        var subject = Render(template?.Subject ?? "LicenseHub · {applicationName} · {eventType}").Replace('\r', ' ').Replace('\n', ' ');
        return (subject[..Math.Min(200, subject.Length)], Render(template?.Body ?? DefaultBody(log.EventType)));
    }
    public static string DefaultBody(string eventType) => eventType switch
    {
        "Activated" => "Tu licencia Premium fue activada.",
        "Renewed" => "Tu licencia Premium fue renovada.",
        "ExpiryApproaching" => "Tu licencia Premium está próxima a vencer.",
        "Expired" => "Tu licencia Premium ha vencido.",
        "Suspended" => "Tu licencia Premium está suspendida.",
        "Revoked" => "Tu licencia Premium fue revocada.",
        _ => "Hay una actualización de tu licencia."
    };
    private static string Code(string type) => type switch { "ExpiryApproaching" => "expiry_approaching", _ => type.ToLowerInvariant() };
}
