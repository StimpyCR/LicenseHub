using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.Application.Licensing;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Notifications;
using LicenseHub.Infrastructure.Notifications;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class MailpitFactAttribute : FactAttribute
{
    public MailpitFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LICENSEHUB_MAILPIT_TESTS") != "1")
            Skip = "Mailpit capture requires explicit opt-in; run test-backend-docker.ps1 -WithMailpit.";
    }
}

public sealed class MailpitDeliveryTests
{
    [MailpitFact]
    public async Task ActivationWarningExpiryAndRenewalReachMailpitWithoutDuplicatesOrLicenseKeys()
    {
        var host = Environment.GetEnvironmentVariable("LICENSEHUB_MAILPIT_HOST") ?? "mailpit";
        var apiUrl = Environment.GetEnvironmentVariable("LICENSEHUB_MAILPIT_URL") ?? "http://mailpit:8025";
        Assert.Contains(host, new[] { "mailpit", "127.0.0.1", "localhost" });
        var uri = new Uri(apiUrl);
        Assert.Equal("http", uri.Scheme);
        Assert.Contains(uri.Host, new[] { "mailpit", "127.0.0.1", "localhost" });
        Assert.Equal(8025, uri.Port);
        await using var f = await LicenseOperationsTests.SetupAsync();
        var recipient = "mailpit-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        var subjectId = "mailpit-" + Guid.NewGuid().ToString("N");
        var customer = await f.Admin(s => s.CreateCustomerAsync(f.Actor, Guid.NewGuid(), new(f.App, subjectId, "Mailpit Test", recipient), "mailpit-test", default));
        var issued = await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { CustomerId = customer.Id }, "mailpit-test", default));
        await using (var templates = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var management = templates.ServiceProvider.GetRequiredService<INotificationManagement>();
            foreach (var eventType in new[] { "Activated", "ExpiryApproaching", "Expired", "Renewed" })
                await management.SaveTemplateAsync(f.Actor, f.App, Guid.NewGuid(),
                    new(eventType, "es", "LicenseHub · {applicationName} · {eventType}", "Evento {eventType}; licencia {licenseId}; negocio {subjectId}.", "0"), "mailpit-test", default);
        }
        var key = issued.Items[0].LicenseKey!;
        var operation = Guid.NewGuid();
        var request = new ActivationRequest { LicenseKey = key, SubjectId = subjectId };
        await f.Integration(s => s.ActivateAsync(f.App, operation, request, "mailpit-test", default));
        await f.Integration(s => s.ActivateAsync(f.App, operation, request, "mailpit-test", default));
        var sender = new SmtpEmailSender(new() { Host = host, Port = 1025, From = "notifications@licensehub.example.invalid", AllowUnencryptedDevelopment = true });
        using var capture = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
        async Task Dispatch()
        {
            await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
            var worker = new NotificationWorker(scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>(), f.Fixture.Clock, sender, scope.ServiceProvider.GetRequiredService<ICursorProtection>());
            await worker.ReconcileAsync(default);
            await worker.DispatchAsync(default);
        }
        async Task Check(string[] events)
        {
            var messages = await capture.GetFromJsonAsync<JsonElement>("/api/v1/search?query=" + Uri.EscapeDataString("to:" + recipient));
            Assert.Equal(events.Length, messages.GetProperty("total").GetInt32());
            var items = messages.GetProperty("messages").EnumerateArray().ToArray();
            foreach (var eventType in events) Assert.Single(items, item => item.GetProperty("Subject").GetString()!.EndsWith(" · " + eventType, StringComparison.Ordinal));
            foreach (var item in items)
            {
                var content = await capture.GetFromJsonAsync<JsonElement>("/api/v1/message/" + item.GetProperty("ID").GetString());
                Assert.DoesNotContain(key, content.GetRawText());
                Assert.Contains(issued.Items[0].License.Id.ToString(), content.GetProperty("Text").GetString());
                Assert.Contains(subjectId, content.GetProperty("Text").GetString());
            }
        }
        await Dispatch(); await Dispatch();
        await Check(["Activated"]);
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Dispatch();
        await Check(["Activated", "ExpiryApproaching"]);
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Dispatch(); await Dispatch();
        await Check(["Activated", "ExpiryApproaching", "Expired"]);
        var expired = await f.Admin(s => s.LicenseAsync(f.Actor, issued.Items[0].License.Id, default));
        var renewal = new RenewalWrite(Guid.NewGuid(), new("Duration", new("Hour", 1)), false, "Mailpit renewal test");
        var renewalKey = Guid.NewGuid();
        await f.Lifecycle(s => s.RenewAsync(f.Actor, expired.Id, renewalKey, expired.Etag, renewal, "mailpit-test", default));
        await f.Lifecycle(s => s.RenewAsync(f.Actor, expired.Id, renewalKey, expired.Etag, renewal, "mailpit-test", default));
        await Dispatch(); await Dispatch();
        await Check(["Activated", "ExpiryApproaching", "Expired", "Renewed"]);
        await using var logsScope = f.Fixture.Factory.Services.CreateAsyncScope();
        var logs = await logsScope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Set<NotificationLogRow>()
            .Where(x => x.ApplicationId == f.App && x.Channel == "Email" && x.State == "Sent").ToArrayAsync();
        Assert.Equal(4, logs.Length);
        Assert.All(logs, log => Assert.Equal(1, log.Attempts));
    }
}
