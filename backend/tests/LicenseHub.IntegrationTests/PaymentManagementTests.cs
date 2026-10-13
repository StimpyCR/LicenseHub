using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Billing;
using LicenseHub.Application.Licensing;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class PaymentManagementTests
{
    [Fact]
    public async Task ConcurrentConfirmationAndRepeatedKeysApplyOnlyOneRenewal()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        var intent = new RenewalWrite(Guid.NewGuid(), new("Duration", new("Day", 1)), false, "Paid renewal");
        var payment = await Payment(f, s => s.CreateAsync(f.Actor, Guid.NewGuid(), new(f.App, license.Id, license.CustomerId!.Value, "10.0000", "USD", "reference-1", intent), "test", default));
        var key = Guid.NewGuid();
        var confirmed = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, key, payment.Etag, "confirm", "Verified evidence", "test", default))));
        Assert.All(confirmed, x => Assert.Equal("Confirmed", x.State));
        Assert.Equal("Confirmed", (await Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, Guid.NewGuid(), payment.Etag, "confirm", "Repeated verification", "test", default))).State);
        var renewed = await f.Admin(s => s.LicenseAsync(f.Actor, license.Id, default));
        Assert.Equal(license.ExpiresAt!.Value.AddDays(1), renewed.ExpiresAt);
        var history = await f.Lifecycle(s => s.RenewalsAsync(f.Actor, license.Id, null, 25, default));
        Assert.Equal(payment.Id, Assert.Single(history.Items).PaymentId);
        var refunded = await Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, Guid.NewGuid(), confirmed[0].Etag, "refund", "Manual refund record", "test", default));
        Assert.Equal("Refunded", refunded.State);
        Assert.True((await f.Integration(s => s.SnapshotAsync(f.App, "billing-business", default))).HasPremium);
        Assert.Equal(renewed.ExpiresAt, (await f.Admin(s => s.LicenseAsync(f.Actor, license.Id, default))).ExpiresAt);
    }

    [Fact]
    public async Task FailedRenewalRollsBackConfirmationAndReferenceCannotBeDuplicated()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        await f.Lifecycle(s => s.ChangeStateAsync(f.Actor, license.Id, Guid.NewGuid(), license.Etag, "cancel", "Cancelled", "test", default));
        var body = new PaymentWrite(f.App, license.Id, license.CustomerId!.Value, "2.5000", "CRC", "reference-2", new(Guid.NewGuid(), new("Duration", new("Month", 1)), false, "Cannot renew cancelled"));
        var payment = await Payment(f, s => s.CreateAsync(f.Actor, Guid.NewGuid(), body, "test", default));
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, Guid.NewGuid(), payment.Etag, "confirm", "Verified", "test", default)))).Status);
        Assert.Equal("Pending", (await Payment(f, s => s.ListAsync(f.Actor, f.App, null, null, 25, default))).Items[0].State);
        Assert.Empty((await f.Lifecycle(s => s.RenewalsAsync(f.Actor, license.Id, null, 25, default))).Items);
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.CreateAsync(f.Actor, Guid.NewGuid(), body, "test", default)))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.CreateAsync(f.Actor, Guid.NewGuid(), body with { Amount = "2,5000" }, "test", default)))).Status);
        var rejected = await Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, Guid.NewGuid(), payment.Etag, "reject", "Invalid receipt", "test", default));
        Assert.Equal("Rejected", rejected.State);
        Assert.Equal(409, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.ChangeAsync(f.Actor, payment.Id, Guid.NewGuid(), rejected.Etag, "confirm", "Cannot reverse rejection", "test", default)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.ListAsync(Guid.NewGuid(), f.App, null, null, 25, default)))).Status);
        var foreign = await f.NewApp("foreign");
        Assert.Equal(404, (await Assert.ThrowsAsync<ManagementException>(() => Payment(f, s => s.CreateAsync(f.Actor, Guid.NewGuid(), body with { ApplicationId = foreign }, "test", default)))).Status);
    }

    [Fact]
    public async Task HttpPaymentRequiresSessionCsrfAndExposesImmutableIntentForReview()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var license = await Active(f);
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/payments?applicationId=" + f.App)).StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = f.Fixture.Email, password = f.Fixture.Password });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(f.Fixture.Key) })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var body = new PaymentWrite(f.App, license.Id, license.CustomerId!.Value, "15.0000", "USD", "receipt-http", new(Guid.NewGuid(), new("Duration", new("Day", 5)), false, "Receipt intent"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/payments", body)).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var created = await client.PostAsJsonAsync("/api/v1/admin/payments", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("15.0000", json.GetProperty("amount").GetString());
        Assert.Equal(5, json.GetProperty("renewalIntent").GetProperty("term").GetProperty("duration").GetProperty("value").GetInt32());
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null) { Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "payment.json"), json.GetRawText()); }
    }

    private static async Task<T> Payment<T>(LicenseOperationsTests.LicenseFixture f, Func<IPaymentManagement, Task<T>> operation)
    {
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<IPaymentManagement>());
    }
    private static async Task<LicenseView> Active(LicenseOperationsTests.LicenseFixture f)
    {
        var issued = await f.Issue();
        await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "billing-business" }, "test", default));
        return await f.Admin(s => s.LicenseAsync(f.Actor, issued.Items[0].License.Id, default));
    }
}
