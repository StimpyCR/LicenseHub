using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Integration;
using LicenseHub.Application.Licensing;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class LicenseOperationsTests
{
    [Fact]
    public async Task BatchCodesAreRandomHashOnlyAndRetryCannotRecoverThem()
    {
        await using var f = await SetupAsync();
        var key = Guid.NewGuid();
        var request = f.Request() with { Quantity = 100 };
        var issued = await f.Admin(s => s.IssueAsync(f.Actor, key, request, "test", default));
        Assert.True(issued.CodesRecoverable);
        Assert.Equal(100, issued.Items.Select(x => x.LicenseKey).Distinct().Count());
        Assert.All(issued.Items, item => Assert.Matches("^LH1_[A-Za-z0-9_-]{43}$", item.LicenseKey!));
        var replay = await f.Admin(s => s.IssueAsync(f.Actor, key, request, "test", default));
        Assert.False(replay.CodesRecoverable);
        Assert.All(replay.Items, item => Assert.Null(item.LicenseKey));
        Assert.Equal(issued.Items.Select(x => x.License.Id), replay.Items.Select(x => x.License.Id));
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var record = await db.Set<IdempotencyRow>().SingleAsync(x => x.Key == key);
        var hashes = await db.Set<LicenseRow>().Select(x => x.KeyHash).ToArrayAsync();
        Assert.All(issued.Items, item => { Assert.DoesNotContain(item.LicenseKey!, record.ResponseJson!); Assert.DoesNotContain(item.LicenseKey!, hashes); });
        Assert.DoesNotContain("keyHash", JsonSerializer.Serialize(issued, JsonSerializerOptions.Web));
        var changed = await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.IssueAsync(f.Actor, key, request with { Quantity = 99 }, "test", default)));
        Assert.Equal("idempotency_conflict", changed.Code);
    }

    [Fact]
    public async Task ConcurrentActivationIsIdempotentAndAnotherKeyCannotRedeemAgain()
    {
        await using var f = await SetupAsync();
        var issued = await f.Issue();
        var key = Guid.NewGuid();
        var request = new ActivationRequest { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "business-1" };
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => f.Integration(s => s.ActivateAsync(f.App, key, request, "test", default))));
        Assert.Single(results.Select(x => x.ActivationId).Distinct());
        Assert.All(results, x => { Assert.True(x.Snapshot.HasPremium); Assert.Equal(["reports.export"], x.Snapshot.Features); });
        Assert.Equal(results[0].Snapshot.EvaluatedAt, results[4].Snapshot.EvaluatedAt);
        var duplicate = await Assert.ThrowsAsync<ManagementException>(() => f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), request, "test", default)));
        Assert.Equal("license_already_activated", duplicate.Code);
        var changed = await Assert.ThrowsAsync<ManagementException>(() => f.Integration(s => s.ActivateAsync(f.App, key,
            new() { LicenseKey = request.LicenseKey, SubjectId = "another" }, "test", default)));
        Assert.Equal("idempotency_conflict", changed.Code);
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Set<LicenseActivationRow>().CountAsync());
    }

    [Fact]
    public async Task TwoMinuteExpiryReplacementAndLifetimeUseCurrentRights()
    {
        await using var f = await SetupAsync();
        var issued = await f.Issue();
        var activationKey = Guid.NewGuid();
        var request = new ActivationRequest { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "business-1" };
        var activated = await f.Integration(s => s.ActivateAsync(f.App, activationKey, request, "test", default));
        Assert.Equal(activated.ActivatedAt.AddMinutes(2), activated.Snapshot.License!.ExpiresAt);
        f.Fixture.Clock.Advance(TimeSpan.FromSeconds(119));
        var lastSecond = await f.Integration(s => s.SnapshotAsync(f.App, "business-1", default));
        Assert.True(lastSecond.HasPremium);
        Assert.Equal(lastSecond.License!.ExpiresAt, lastSecond.CacheUntil);
        f.Fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await f.Integration(s => s.SnapshotAsync(f.App, "business-1", default));
        Assert.Equal("Expired", expired.EffectiveState);
        Assert.False(expired.HasPremium);
        Assert.Empty(expired.Features);
        // Replay remains historical, so consumers must query current rights.
        Assert.True((await f.Integration(s => s.ActivateAsync(f.App, activationKey, request, "test", default))).Snapshot.HasPremium);
        var lifetime = await f.Issue(new("Lifetime"));
        var replacement = await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = lifetime.Items[0].LicenseKey!, SubjectId = "business-1" }, "test", default));
        Assert.True(replacement.Snapshot.License!.IsLifetime);
        Assert.Null(replacement.Snapshot.License.ExpiresAt);
        f.Fixture.Clock.Advance(TimeSpan.FromDays(500));
        Assert.True((await f.Integration(s => s.SnapshotAsync(f.App, "business-1", default))).HasPremium);
    }

    [Fact]
    public async Task TwoDifferentLicensesRacingForSameSubjectHaveOnlyOneWinner()
    {
        await using var f = await SetupAsync();
        var batch = await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { Quantity = 2 }, "test", default));
        async Task<bool> Activate(IssueItem item)
        {
            try { await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = item.LicenseKey!, SubjectId = "same" }, "test", default)); return true; }
            catch (ManagementException e) { Assert.Equal("subject_has_license", e.Code); return false; }
        }
        var result = await Task.WhenAll(batch.Items.Select(Activate));
        Assert.Equal(1, result.Count(x => x));
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        Assert.Equal(1, await db.Set<CustomerRow>().CountAsync());
        Assert.Equal(1, await db.Set<LicenseActivationRow>().CountAsync());
    }

    [Fact]
    public async Task CustomersAndPreassignmentAreTenantBoundAndContactUsesConcurrency()
    {
        await using var f = await SetupAsync();
        var customerKey = Guid.NewGuid();
        var body = new CustomerWrite(f.App, "reserved", "Customer", "customer@example.invalid");
        var customer = await f.Admin(s => s.CreateCustomerAsync(f.Actor, customerKey, body, "test", default));
        Assert.Equal(customer.Id, (await f.Admin(s => s.CreateCustomerAsync(f.Actor, customerKey, body, "test", default))).Id);
        var updated = await f.Admin(s => s.UpdateCustomerAsync(f.Actor, customer.Id, Guid.NewGuid(), customer.Etag, new("Updated", null), "test", default));
        Assert.Equal("\"2\"", updated.Etag);
        var stale = await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.UpdateCustomerAsync(f.Actor, customer.Id, Guid.NewGuid(), customer.Etag, new(null, null), "test", default)));
        Assert.Equal(412, stale.Status);
        var issued = await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { CustomerId = customer.Id }, "test", default));
        var mismatch = await Assert.ThrowsAsync<ManagementException>(() => f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "other" }, "test", default)));
        Assert.Equal("license_subject_mismatch", mismatch.Code);
        Assert.True((await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = issued.Items[0].LicenseKey!, SubjectId = "reserved" }, "test", default))).Snapshot.HasPremium);
        var foreign = await f.NewApp("foreign");
        var denied = await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { ApplicationId = foreign, CustomerId = customer.Id, Features = [] }, "test", default)));
        Assert.Equal(404, denied.Status);
        var deniedActor = await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.LicenseAsync(Guid.NewGuid(), issued.Items[0].License.Id, default)));
        Assert.Equal(404, deniedActor.Status);
        Assert.Null((await f.Integration(s => s.SnapshotAsync(foreign, "reserved", default))).License);
        await f.Admin(s => s.CreateCustomerAsync(f.Actor, Guid.NewGuid(), body with { SubjectId = "second" }, "test", default));
        var first = await f.Admin(s => s.CustomersAsync(f.Actor, f.App, null, 1, default));
        Assert.NotNull(first.NextCursor);
        var badCursor = await Assert.ThrowsAsync<ManagementException>(() => f.Admin(s => s.CustomersAsync(f.Actor, foreign, first.NextCursor, 1, default)));
        Assert.Equal(400, badCursor.Status);
    }

    [Fact]
    public async Task ExpiredIssueAndDisabledFeaturesAndInvalidTermsDenyRights()
    {
        await using var f = await SetupAsync();
        var immediate = await f.Admin(s => s.IssueAsync(f.Actor, Guid.NewGuid(), f.Request() with { StartMode = "OnIssue" }, "test", default));
        f.Fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var expired = await Assert.ThrowsAsync<ManagementException>(() => f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = immediate.Items[0].LicenseKey!, SubjectId = "expired" }, "test", default)));
        Assert.Equal("license_not_activatable", expired.Code);
        foreach (var invalid in new[] { new TermWrite("Until", null, f.Fixture.Clock.GetUtcNow().AddDays(1)), new("Duration", new("Month", 1000000)), new("Lifetime", new("Day", 1)) })
            Assert.Equal(400, (await Assert.ThrowsAsync<ManagementException>(() => f.Issue(invalid))).Status);
        var active = await f.Issue();
        await f.Integration(s => s.ActivateAsync(f.App, Guid.NewGuid(), new() { LicenseKey = active.Items[0].LicenseKey!, SubjectId = "features" }, "test", default));
        await using var scope = f.Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
        var feature = await db.Set<ApplicationFeatureRow>().SingleAsync();
        feature.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty((await f.Integration(s => s.SnapshotAsync(f.App, "features", default))).Features);
        var license = await db.Set<LicenseRow>().SingleAsync(x => x.Id == active.Items[0].License.Id);
        license.State = LicenseState.Suspended;
        await db.SaveChangesAsync();
        Assert.False((await f.Integration(s => s.SnapshotAsync(f.App, "features", default))).HasPremium);
        license.State = LicenseState.Revoked;
        await db.SaveChangesAsync();
        Assert.Equal("Revoked", (await f.Integration(s => s.SnapshotAsync(f.App, "features", default))).EffectiveState);
    }

    [Fact]
    public async Task HttpIntegrationValidatesScopesTenantAndJsonContract()
    {
        await using var f = await SetupAsync();
        var code = (await f.Issue()).Items[0].LicenseKey!;
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/licenses/status?subjectId=business")).StatusCode);
        var read = await f.Credential([IntegrationScopes.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", read);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/licenses/activate", new { licenseKey = code, subjectId = "business" })).StatusCode);
        var noneResponse = await client.GetAsync("/api/v1/licenses/status?subjectId=business");
        Assert.Equal(HttpStatusCode.OK, noneResponse.StatusCode);
        var none = await noneResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, none.GetProperty("license").ValueKind);
        Assert.Equal("None", none.GetProperty("effectiveState").GetString());
        var both = await f.Credential([IntegrationScopes.Read, IntegrationScopes.Activate]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", both);
        var response = await client.PostAsJsonAsync("/api/v1/licenses/activate", new { licenseKey = code, subjectId = "business" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith("Z", result.GetProperty("activatedAt").GetString());
        var snapshot = result.GetProperty("snapshot");
        Assert.Equal(JsonValueKind.String, snapshot.GetProperty("entitlementVersion").ValueKind);
        Assert.True(snapshot.GetProperty("hasPremium").GetBoolean());
        Assert.Equal(f.App.ToString(), snapshot.GetProperty("applicationId").GetString());
        Assert.DoesNotContain(code, result.GetRawText());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var foreign = await f.NewApp("foreign");
        var foreignCredential = await f.Credential([IntegrationScopes.Read, IntegrationScopes.Activate], foreign);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignCredential);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/licenses/activate", new { licenseKey = code, subjectId = "business" })).StatusCode);
        var unrelated = await client.GetFromJsonAsync<JsonElement>("/api/v1/licenses/status?subjectId=business");
        Assert.False(unrelated.GetProperty("hasPremium").GetBoolean());
        // Persist actual HTTP results for JSON Schema/OpenAPI validation in CI and locally.
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "activation.json"), result.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "snapshot.json"), snapshot.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "none.json"), none.GetRawText());
        }
    }

    [Fact]
    public async Task HttpAdministrationEnforcesCsrfStrictTermsAndOneTimeCodes()
    {
        await using var f = await SetupAsync();
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = f.Fixture.Email, password = f.Fixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/admin/auth/mfa", new { code = SuperAdminBootstrapTests.Totp(f.Fixture.Key) })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/licenses", f.Request())).StatusCode);
        csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/licenses", new
        {
            applicationId = f.App,
            quantity = 1,
            startMode = "OnActivation",
            features = new[] { "reports.export" },
            term = new { kind = "Lifetime", duration = (object?)null }
        })).StatusCode);
        var issued = await client.PostAsJsonAsync("/api/v1/admin/licenses", f.Request());
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var value = await issued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(value.GetProperty("codesRecoverable").GetBoolean());
        var replay = await client.PostAsJsonAsync("/api/v1/admin/licenses", f.Request());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var repeated = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(repeated.GetProperty("codesRecoverable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, repeated.GetProperty("items")[0].GetProperty("licenseKey").ValueKind);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var customerResponse = await client.PostAsJsonAsync("/api/v1/admin/customers", new CustomerWrite(f.App, "customer-1", "Name", null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.PutAsJsonAsync("/api/v1/admin/customers/" + customer.GetProperty("id").GetString(), new CustomerContactWrite("Updated", null))).StatusCode);
        var output = Environment.GetEnvironmentVariable("LICENSEHUB_CONTRACT_RESULTS_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            // Codes are secrets even for synthetic licenses; redact before persisting HTTP fixtures.
            var safe = value.Deserialize<IssueResponse>(JsonSerializerOptions.Web)!;
            var redacted = new IssueResponse { CodesRecoverable = false, Items = safe.Items.Select(x => new IssueItem { License = x.License, LicenseKey = null }).ToArray() };
            await File.WriteAllTextAsync(Path.Combine(output, "issue.json"), JsonSerializer.Serialize(redacted, JsonSerializerOptions.Web));
            await File.WriteAllTextAsync(Path.Combine(output, "issue-replay.json"), repeated.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(output, "customer.json"), customer.GetRawText());
        }
    }

    private static async Task<LicenseFixture> SetupAsync()
    {
        var fixture = await AdminAuthenticationTests.AuthFixture.CreateAsync();
        try
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var actor = await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Users.Where(x => x.Email == fixture.Email).Select(x => x.Id).SingleAsync();
            var management = scope.ServiceProvider.GetRequiredService<IApplicationManagement>();
            var app = await management.CreateAsync(actor, Guid.NewGuid(), new("StockMini", "stockmini", true), "test", default);
            await management.SaveFeatureAsync(actor, app.Id, null, Guid.NewGuid(), null, new("reports.export", "Reports", true), "test", default);
            return new(fixture, actor, app.Id);
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private sealed class LicenseFixture(AdminAuthenticationTests.AuthFixture fixture, Guid actor, Guid app) : IAsyncDisposable
    {
        public AdminAuthenticationTests.AuthFixture Fixture { get; } = fixture;
        public Guid Actor { get; } = actor;
        public Guid App { get; } = app;
        public IssueRequest Request(TermWrite? term = null) => new(App, 1, "OnActivation", term ?? new("Duration", new("Minute", 2)), ["reports.export"]);
        public Task<IssueResponse> Issue(TermWrite? term = null) => Admin(s => s.IssueAsync(Actor, Guid.NewGuid(), Request(term), "test", default));
        public async Task<T> Admin<T>(Func<ILicenseManagement, Task<T>> action)
        {
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<ILicenseManagement>());
        }
        public async Task<T> Integration<T>(Func<ILicenseIntegration, Task<T>> action)
        {
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<ILicenseIntegration>());
        }
        public async Task<Guid> NewApp(string slug)
        {
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IApplicationManagement>().CreateAsync(Actor, Guid.NewGuid(), new(slug, slug, true), "test", default)).Id;
        }
        public async Task<string> Credential(string[] scopes, Guid? application = null)
        {
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IApplicationManagement>().IssueAsync(Actor, application ?? App, Guid.NewGuid(), new(scopes, Fixture.Clock.GetUtcNow().AddHours(1)), "test", default)).Secret;
        }
        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
