using System.Net.Http.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class LicenseLifecycleTests
{
    [Fact]
    public async Task ConcurrentRenewalsExtendOnceAndHistoryCannotBeChanged()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        var key = Guid.NewGuid();
        var request = new RenewalWrite(Guid.NewGuid(), new("Duration", new("Hour", 1)), false, "Manual renewal");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, key, license.Etag, request, "test", default))));
        Assert.All(results, x => Assert.Equal(license.ExpiresAt!.Value.AddHours(1), x.ExpiresAt));
        Assert.Single(results.Select(x => x.Etag).Distinct());
        var duplicate = await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), results[0].Etag, request, "test", default)));
        Assert.Equal("renewal_already_applied", duplicate.Code);
        var history = await f.Lifecycle(s => s.RenewalsAsync(f.Actor, license.Id, null, 25, default));
        var renewal = Assert.Single(history.Items);
        Assert.Equal(license.ExpiresAt, renewal.BeforeExpiresAt);
        Assert.Equal(results[0].ExpiresAt, renewal.AfterExpiresAt);
        Assert.Single((await f.Lifecycle(s => s.ActivationsAsync(f.Actor, license.Id, null, 25, default))).Items);
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"LicenseRenewals\" SET \"Reason\" = 'changed' WHERE \"Id\" = {renewal.Id}"));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal("Manual renewal", (await f.Lifecycle(s => s.RenewalsAsync(f.Actor, license.Id, null, 25, default))).Items[0].Reason);
    }

    [Fact]
    public async Task ExpiredRenewalUsesNowAndCalendarMonthsClampAndLifetimeConversionWorks()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        f.Fixture.Clock.Advance(TimeSpan.FromDays(1));
        var expired = await f.Admin(s => s.LicenseAsync(f.Actor, license.Id, default));
        var renewed = await f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), expired.Etag,
            new(Guid.NewGuid(), new("Duration", new("Minute", 2)), false, "Expired renewal"), "test", default));
        Assert.Equal(f.Fixture.Clock.GetUtcNow().AddMinutes(2).UtcDateTime, renewed.ExpiresAt);
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            var row = await db.Set<LicenseRow>().FindAsync(license.Id);
            row!.ExpiresAt = new DateTimeOffset(f.Fixture.Clock.GetUtcNow().Year + 1, 1, 31, 10, 30, 0, TimeSpan.Zero);
            row.Version++;
            await db.SaveChangesAsync();
        }
        var calendar = await f.Admin(s => s.LicenseAsync(f.Actor, license.Id, default));
        var month = await f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), calendar.Etag,
            new(Guid.NewGuid(), new("Duration", new("Month", 1)), false, "Calendar month"), "test", default));
        Assert.Equal(calendar.ExpiresAt!.Value.AddMonths(1), month.ExpiresAt);
        Assert.Equal(2, month.ExpiresAt!.Value.Month);
        var shorter = await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), month.Etag,
            new(Guid.NewGuid(), new("Until", null, new DateTimeOffset(month.ExpiresAt!.Value, TimeSpan.Zero).AddMinutes(-1)), false, "Shorter"), "test", default)));
        Assert.Equal(400, shorter.Status);
        var lifetime = await f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), month.Etag,
            new(Guid.NewGuid(), new("Lifetime"), false, "Lifetime upgrade"), "test", default));
        Assert.True(lifetime.IsLifetime);
        Assert.Null(lifetime.ExpiresAt);
        Assert.True((await f.Integration(s => s.SnapshotAsync(f.App, "business", default))).HasPremium);
    }

    [Fact]
    public async Task StateChangesDenyPremiumRequireExplicitRenewalAndCancellationIsTerminal()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        var suspended = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), license.Etag, "suspend", "Review", "test", default));
        Assert.Equal("Suspended", suspended.State);
        Assert.False((await f.Integration(s => s.SnapshotAsync(f.App, "business", default))).HasPremium);
        var restored = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), suspended.Etag, "reactivate", "Approved", "test", default));
        Assert.Equal("Active", restored.State);
        var revoked = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), restored.Etag, "revoke", "Revocation", "test", default));
        var denied = await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), revoked.Etag,
            new(Guid.NewGuid(), new("Duration", new("Day", 1)), false, "No reactivation"), "test", default)));
        Assert.Equal("invalid_transition", denied.Code);
        var renewed = await f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), revoked.Etag,
            new(Guid.NewGuid(), new("Duration", new("Day", 1)), true, "Authorized restore"), "test", default));
        Assert.True((await f.Lifecycle(s => s.RenewalsAsync(f.Actor, license.Id, null, 25, default))).Items[0].Reactivated);
        var cancelled = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), renewed.Etag, "cancel", "Permanent cancellation", "test", default));
        Assert.Equal("Cancelled", cancelled.State);
        Assert.False((await f.Integration(s => s.SnapshotAsync(f.App, "business", default))).HasPremium);
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, license.Id, Guid.NewGuid(), cancelled.Etag,
            new(Guid.NewGuid(), new("Lifetime"), true, "Cannot restore"), "test", default)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.ChangeStateAsync(Guid.NewGuid(), license.Id, Guid.NewGuid(), cancelled.Etag, "cancel", "Foreign actor", "test", default)))).Status);
    }

    [Fact]
    public async Task RenewingOldLicenseCannotDisplaceAnotherActiveLicenseAndPendingCannotRenew()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var original = await Active(f);
        var revoked = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, original.Id, Guid.NewGuid(), original.Etag, "revoke", "Replace", "test", default));
        var replacement = await Active(f);
        var conflict = await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, original.Id, Guid.NewGuid(), revoked.Etag,
            new(Guid.NewGuid(), new("Lifetime"), true, "Restore old"), "test", default)));
        Assert.Equal("subject_has_license", conflict.Code);
        Assert.Equal(replacement.Id, (await f.Integration(s => s.SnapshotAsync(f.App, "business", default))).License!.Id);
        var pending = (await f.Issue()).Items[0].License;
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(f.Actor, pending.Id, Guid.NewGuid(), pending.Etag,
            new(Guid.NewGuid(), new("Lifetime"), false, "Pending renewal"), "test", default)))).Status);
        var suspended = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, pending.Id, Guid.NewGuid(), pending.Etag, "suspend", "Hold unredeemed", "test", default));
        Assert.Equal("Pending", (await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, pending.Id, Guid.NewGuid(), suspended.Etag, "reactivate", "Release", "test", default))).State);
    }

    [Fact]
    public async Task DelegatedRenewalCannotReactivateWithoutSeparateGrantOrCrossApplications()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        var suspended = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), license.Etag, "suspend", "Hold", "test", default));
        Guid delegated;
        await using (var scope = f.Fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IAdminUsers>();
            var pending = await users.CreateAsync(f.Actor, Guid.NewGuid(), new("renewal-delegate@example.invalid"), "test", default);
            await users.UpdateAsync(f.Actor, pending.Id, pending.Etag, new(false, [new(f.App, "licenses:renew")], "Limited renewal authority"), "test", default);
            var secret = SuperAdminBootstrapTests.Base32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
            await scope.ServiceProvider.GetRequiredService<IAdminEnrollment>().EnrollAsync(new()
            {
                UserId = pending.Id,
                Email = pending.Email,
                Confirmation = "ENROLL:" + pending.Id,
                Password = "aA1!" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                AuthenticatorKey = secret,
                Code = SuperAdminBootstrapTests.Totp(secret),
                Reason = "Enroll limited operator"
            }, "test", default);
            delegated = pending.Id;
        }
        var request = new RenewalWrite(Guid.NewGuid(), new("Duration", new("Day", 1)), true, "Cannot escalate");
        var denied = await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(delegated, license.Id, Guid.NewGuid(), suspended.Etag, request, "test", default)));
        Assert.Equal(404, denied.Status);
        var active = await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), suspended.Etag, "reactivate", "Root approval", "test", default));
        var extended = await f.Lifecycle(s => s.RenewAsync(delegated, license.Id, Guid.NewGuid(), active.Etag, request with { Reactivate = false }, "test", default));
        Assert.Equal(active.ExpiresAt!.Value.AddDays(1), extended.ExpiresAt);
        var foreign = await f.NewApp("foreign");
        var other = (await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { ApplicationId = foreign, Features = [] }, "test", default))).Items[0].License;
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => f.Lifecycle(s => s.RenewAsync(delegated, other.Id, Guid.NewGuid(), other.Etag, request, "test", default)))).Status);
    }

    [Fact]
    public async Task HttpStateChangesUseCsrfEtagAndContractAndReplay()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = f.Fixture.Email, password = f.Fixture.Password });
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(f.Fixture.Key) })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var path = "/api/v1/admin/licenses/" + license.Id + "/suspend";
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { reason = "Hold" })).StatusCode);
        csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(System.Net.HttpStatusCode.PreconditionRequired, (await client.PostAsJsonAsync(path, new { reason = "Hold" })).StatusCode);
        client.DefaultRequestHeaders.Add("If-Match", license.Etag);
        var response = await client.PostAsJsonAsync(path, new { reason = "Hold" });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Suspended", json.GetProperty("state").GetString());
        Assert.Equal(json.GetProperty("etag").GetString(), response.Headers.ETag!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { reason = "Hold" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Remove("If-Match");
        client.DefaultRequestHeaders.Add("If-Match", json.GetProperty("etag").GetString());
        var renew = await client.PostAsJsonAsync("/api/v1/admin/licenses/" + license.Id + "/renew",
            new RenewalWrite(Guid.NewGuid(), new("Duration", new("Day", 1)), true, "Authorized renewal"));
        Assert.Equal(System.Net.HttpStatusCode.OK, renew.StatusCode);
        var history = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/licenses/" + license.Id + "/renewals");
        Assert.Equal(1, history.GetProperty("items").GetArrayLength());
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "lifecycle.json"), json.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "renewal.json"), history.GetProperty("items")[0].GetRawText());
        }
    }

    private static async Task<LicenseView> Active(LicenseOperationsTests.LicenseFixture f)
    {
        var issued = await f.Issue();
        await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "business" }, "test", default));
        return await f.Admin(s => s.LicenseAsync(f.Actor, issued.Items[0].License.Id, default));
    }
}
