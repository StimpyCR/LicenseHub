using System.Net.Http.Json;
using System.Collections.Concurrent;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Application.Notifications;
using LicenseHub.Infrastructure.Notifications;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class NotificationTests
{
    [Fact]
    public async Task ConcurrentJobsDeduplicateDeliveryAndTwoMinuteExpiryNotices()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await Activate(f);
        var sender = new TestSender();
        await Task.WhenAll(Run(f, sender, w => w.ReconcileAsync(default)), Run(f, sender, w => w.ReconcileAsync(default)));
        await Task.WhenAll(Run(f, sender, w => w.DispatchAsync(default)), Run(f, sender, w => w.DispatchAsync(default)));
        Assert.Single(sender.Accepted);
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        Assert.Equal(2, sender.Accepted.Count);
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        Assert.Equal(3, sender.Accepted.Count);
        var warnings = await Warnings(f, f.App, "notify-business");
        Assert.Contains(warnings.Items, x => x.Code == "expiry_approaching");
        Assert.Single(warnings.Items, x => x.Code == "expired");
        var foreign = await f.NewApp("foreign");
        Assert.Empty((await Warnings(f, foreign, "notify-business")).Items);
        Assert.Empty((await Warnings(f, f.App, "someone-else")).Items);
        Assert.False((await f.Integration(s => s.SnapshotAsync(f.App, "notify-business", default))).HasPremium);
    }

    [Fact]
    public async Task FailuresAreRedactedRetriedAndNeverReportedAsSentEarly()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await Activate(f);
        var sender = new TestSender(1);
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        Assert.Empty(sender.Accepted);
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var failed = await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Set<NotificationLogRow>().SingleAsync(x => x.State == "Failed");
            Assert.Equal("delivery_failed", failed.LastErrorRedacted);
            Assert.Equal(1, failed.Attempts);
            Assert.Null(failed.ProviderId);
        }
        await Run(f, sender, w => w.DispatchAsync(default));
        Assert.Equal(1, sender.Calls);
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Run(f, sender, w => w.DispatchAsync(default));
        Assert.NotEmpty(sender.Accepted);
        await using var final = f.Fixture.Factory.Services.CreateAsyncScope();
        var logs = await final.ServiceProvider.GetRequiredService<INotificationManagement>().LogsAsync(f.Actor, f.App, null, 100, default);
        Assert.DoesNotContain("provider-password", System.Text.Json.JsonSerializer.Serialize(logs));
        Assert.DoesNotContain(logs.Items, x => x.State == "Failed");
    }

    [Fact]
    public async Task RenewalCancelsOldWarningsAndLifetimeHasNoExpirySchedule()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Activate(f);
        var sender = new TestSender();
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        await f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), license.Etag,
            new(Guid.NewGuid(), new("Lifetime"), false, "Lifetime"), "test", default));
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        f.Fixture.Clock.Advance(TimeSpan.FromDays(10));
        await Run(f, sender, w => w.ReconcileAsync(default)); await Run(f, sender, w => w.DispatchAsync(default));
        Assert.Equal(2, sender.Accepted.Count);
        var warnings = await Warnings(f, f.App, "notify-business");
        Assert.Equal("renewed", Assert.Single(warnings.Items).Code);
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        var logs = await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Set<NotificationLogRow>().ToArrayAsync();
        Assert.All(logs.Where(x => x.EventType is "ExpiryApproaching" or "Expired"), x => Assert.Equal("Cancelled", x.State));
    }

    [Fact]
    public async Task SettingsAndTemplatesValidateConcurrencyAndUnconfiguredSmtpFailsHonestly()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var management = scope.ServiceProvider.GetRequiredService<INotificationManagement>();
            var initial = await management.SettingsAsync(f.Actor, f.App, default);
            Assert.Equal([604800, 259200, 86400], initial.Value.OffsetSeconds);
            Assert.Equal("\"0\"", initial.Etag);
            var saved = await management.SaveSettingsAsync(f.Actor, f.App, Guid.NewGuid(), initial.Etag, new([604800, 259200, 86400], [60], true, true), "test", default);
            Assert.Equal("\"1\"", saved.Etag);
            Assert.Equal(412, (await Assert.ThrowsAsync<ManagementException>(() => management.SaveSettingsAsync(f.Actor, f.App, Guid.NewGuid(), initial.Etag, saved.Value, "test", default))).Status);
            var template = await management.SaveTemplateAsync(f.Actor, f.App, Guid.NewGuid(), new("Activated", "es", "Activación {applicationName}", "Negocio {subjectId}: {eventType}", "0"), "test", default);
            Assert.Equal("1", template.Version);
            Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => management.SaveTemplateAsync(f.Actor, f.App, Guid.NewGuid(), new("Activated", "es", "{password}", "Body", "1"), "test", default))).Status);
        }
        await Activate(f);
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<INotificationWorker>();
            await worker.ReconcileAsync(default); await worker.DispatchAsync(default);
        }
        await using var check = f.Fixture.Factory.Services.CreateAsyncScope();
        var logs = await check.ServiceProvider.GetRequiredService<INotificationManagement>().LogsAsync(f.Actor, f.App, null, 100, default);
        Assert.Contains(logs.Items, x => x.State == "Failed" && x.LastError == "mail_not_configured");
        Assert.Contains(logs.Items, x => x.Channel == "InApp" && x.State == "Sent");
    }

    [Fact]
    public async Task HttpWarningsUseTheirScopeAndAdminSettingsTemplatesLogsMatchContract()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await Activate(f);
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<INotificationWorker>();
            await worker.ReconcileAsync(default); await worker.DispatchAsync(default);
        }
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var readOnly = await f.Credential([LicenseHub.Application.Integration.IntegrationScopes.Read]);
        client.DefaultRequestHeaders.Authorization = new("Bearer", readOnly);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/licenses/warnings?subjectId=notify-business")).StatusCode);
        var allowed = await f.Credential([LicenseHub.Application.Integration.IntegrationScopes.Warnings]);
        client.DefaultRequestHeaders.Authorization = new("Bearer", allowed);
        var warningPage = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/licenses/warnings?subjectId=notify-business");
        Assert.Equal("activated", warningPage.GetProperty("items")[0].GetProperty("code").GetString());
        var csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = f.Fixture.Email, password = f.Fixture.Password });
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(f.Fixture.Key) })).StatusCode);
        var settings = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/applications/" + f.App + "/notification-settings");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var templateResponse = await client.PostAsJsonAsync("/api/v1/admin/applications/" + f.App + "/notification-templates", new TemplateWrite("Activated", "es", "Actualizado", "Licencia {licenseId}", "0"));
        Assert.Equal(System.Net.HttpStatusCode.OK, templateResponse.StatusCode);
        var template = await templateResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var logs = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/notifications?applicationId=" + f.App);
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "warning.json"), warningPage.GetProperty("items")[0].GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "notification-settings.json"), settings.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "notification-template.json"), template.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "notification-log.json"), logs.GetProperty("items")[0].GetRawText());
        }
    }

    private static async Task<LicenseView> Activate(LicenseOperationsTests.LicenseFixture f)
    {
        var customer = await f.Admin(s => s.CreateCustomerAsync(f.Actor, Guid.NewGuid(), new(f.App, "notify-business", "Customer", "customer@example.invalid"), "test", default));
        var issued = await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { CustomerId = customer.Id }, "test", default));
        await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "notify-business" }, "test", default));
        return await f.Admin(s => s.LicenseAsync(f.Actor, issued.Items[0].License.Id, default));
    }
    private static async Task Run(LicenseOperationsTests.LicenseFixture f, IEmailSender sender, Func<INotificationWorker, Task> action)
    {
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        await action(new NotificationWorker(scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>(), f.Fixture.Clock, sender, scope.ServiceProvider.GetRequiredService<ICursorProtection>()));
    }
    private static async Task<WarningPage> Warnings(LicenseOperationsTests.LicenseFixture f, Guid app, string subject)
    {
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWarningQueries>().ListAsync(app, subject, null, 100, default);
    }
    private sealed class TestSender(int failures = 0) : IEmailSender
    {
        private int remaining = failures;
        private int calls;
        public int Calls => calls;
        public ConcurrentDictionary<Guid, EmailMessage> Accepted { get; } = new();
        public Task<string> SendAsync(EmailMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            if (Interlocked.Decrement(ref remaining) >= 0) throw new InvalidOperationException("provider-password must never be logged");
            Assert.True(Accepted.TryAdd(message.Id, message));
            return Task.FromResult(message.Id.ToString());
        }
    }
}
