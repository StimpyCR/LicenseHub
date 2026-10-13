using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.IntegrationTests;

public sealed class AdminReportsTests
{
    [Fact]
    public async Task OversizedExportFailsWithoutTruncatingOrRecordingSuccess()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Licenses" ("Id","ApplicationId","KeyHash","State","StartMode","IsLifetime","EntitlementVersion","CreatedAt","Version")
                SELECT gen_random_uuid(), {f.App}, md5(random()::text || series::text), 'Pending', 'OnActivation', true, 1, now(), 1
                FROM generate_series(1,10001) AS series
                """);
        }
        var error = await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.ExportAsync(f.Actor, f.App, "oversized", default)));
        Assert.Equal("export_too_large", error.Code);
        Assert.Empty((await Reports(f, s => s.AuditAsync(f.Actor, f.App, "licenses.exported", null, 25, default))).Items);
    }
    [Fact]
    public async Task DelegatedReportGrantsStayPairedAndCustomerSearchIsLiteral()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var other = await f.NewApp("report-permissions");
        var actor = Guid.NewGuid();
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            Assert.True((await users.CreateAsync(new AdminUser { Id = actor, UserName = "reports@example.invalid", Email = "reports@example.invalid", TwoFactorEnabled = true })).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            db.Add(new ApplicationUserGrantRow { UserId = actor, ApplicationId = f.App, Capability = "dashboard:read" });
            db.Add(new ApplicationUserGrantRow { UserId = actor, ApplicationId = other, Capability = "licenses:export" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, (await Reports(f, s => s.DashboardAsync(actor, f.App, default))).Customers);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.DashboardAsync(actor, other, default)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.ExportAsync(actor, f.App, "denied", default)))).Status);
        await f.Admin(s => s.CreateCustomerAsync(f.Actor, Guid.NewGuid(), new(f.App, "search-a", "100% actual", null), "test", default));
        await f.Admin(s => s.CreateCustomerAsync(f.Actor, Guid.NewGuid(), new(f.App, "search-b", "Actual customer", null), "test", default));
        var literal = await f.Admin(s => s.CustomersAsync(f.Actor, f.App, null, 25, default, "%"));
        Assert.Equal("search-a", Assert.Single(literal.Items).SubjectId);
        var page = await f.Admin(s => s.CustomersAsync(f.Actor, f.App, null, 1, default, "ACTUAL"));
        Assert.NotNull(page.NextCursor);
        Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.CustomersAsync(f.Actor, f.App, page.NextCursor, 1, default, "%")))).Status);
    }
    [Fact]
    public async Task EffectiveCountsAndCsvExcludeExpiredRightsAndSecrets()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var issued = await f.Issue();
        await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "reports-business" }, "test", default));
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            var license = await db.Set<LicenseRow>().FindAsync(issued.Items[0].License.Id);
            f.Fixture.Clock.Advance(TimeSpan.FromMinutes(2));
            var customer = await db.Set<CustomerRow>().FindAsync(license!.CustomerId);
            customer!.DisplayName = "  =HYPERLINK(\"bad\")";
            await db.SaveChangesAsync();
        }
        var dashboard = await Reports(f, s => s.DashboardAsync(f.Actor, f.App, default));
        Assert.Equal(0, dashboard.ActiveLicenses);
        Assert.Equal(0, dashboard.ExpiringWithinSevenDays);
        Assert.Equal(1, dashboard.Customers);
        var pending = await f.Issue();
        var data = Encoding.UTF8.GetString(await Reports(f, s => s.ExportAsync(f.Actor, f.App, "csv-test", default)));
        Assert.Contains("\"Expired\"", data);
        Assert.Contains("\"'  =HYPERLINK", data);
        Assert.DoesNotContain(issued.Items[0].LicenseKey!, data);
        Assert.DoesNotContain("KeyHash", data);
        Assert.Contains(pending.Items[0].License.Id.ToString(), data);
        var audit = await Reports(f, s => s.AuditAsync(f.Actor, f.App, "licenses.exported", null, 25, default));
        Assert.Equal("csv-test", Assert.Single(audit.Items).TraceId);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.ExportAsync(Guid.NewGuid(), f.App, "denied", default)))).Status);
    }

    [Fact]
    public async Task AuditIsScopedAndCursorCannotCrossApplicationOrFilter()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await f.Issue(); await f.Issue();
        var foreign = await f.NewApp("reports-foreign");
        var page = await Reports(f, s => s.AuditAsync(f.Actor, f.App, null, null, 1, default));
        Assert.NotNull(page.NextCursor);
        Assert.All(page.Items, x => Assert.Equal(f.App, x.ApplicationId));
        Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.AuditAsync(f.Actor, foreign, null, page.NextCursor, 1, default)))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.AuditAsync(f.Actor, f.App, "license.issued", page.NextCursor, 1, default)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.DashboardAsync(Guid.NewGuid(), f.App, default)))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => Reports(f, s => s.AuditAsync(f.Actor, f.App, null, null, 101, default)))).Status);
    }

    [Fact]
    public async Task HttpReportsRequireMfaSessionAndMatchContract()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        await f.Issue();
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/dashboard?applicationId=" + f.App)).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = f.Fixture.Email, password = f.Fixture.Password });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(f.Fixture.Key) })).StatusCode);
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/dashboard?applicationId=" + f.App);
        Assert.Equal(1, dashboard.GetProperty("pendingLicenses").GetInt32());
        var csv = await client.GetAsync("/api/v1/admin/licenses/export?applicationId=" + f.App);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(csv.Content.Headers.ContentDisposition);
        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-logs?applicationId=" + f.App + "&action=licenses.exported");
        var row = Assert.Single(audit.GetProperty("items").EnumerateArray());
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "dashboard.json"), dashboard.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "audit.json"), row.GetRawText());
        }
    }
    private static async Task<T> Reports<T>(LicenseOperationsTests.LicenseFixture f, Func<IAdminReports, Task<T>> action)
    {
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAdminReports>());
    }
}
