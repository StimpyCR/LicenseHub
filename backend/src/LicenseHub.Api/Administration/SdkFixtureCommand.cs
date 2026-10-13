using System.Text.Json;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Application.Notifications;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Api.Administration;

// Private development CLI: coordinates the same application services as the API.
// It never migrates storage, resets users or prints credentials/license keys.
public static class SdkFixtureCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment, bool transition, CancellationToken ct)
    {
        try
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) throw new InvalidOperationException("sdk_fixture_nonproduction_only");
            var path = configuration["SdkFixture:StateFile"];
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 65536) throw new InvalidOperationException("sdk_fixture_private_file_required");
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            await db.Database.OpenConnectionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(hashtextextended('licensehub-sdk-integration-fixture', 0))", ct);
            try
            {
                var state = JsonSerializer.Deserialize<SdkFixtureState>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidOperationException("sdk_fixture_invalid_input");
                if (state.SchemaVersion != 1 || state.RunId == Guid.Empty || state.ActorId == Guid.Empty) throw new InvalidOperationException("sdk_fixture_invalid_input");
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
                var actor = await users.FindByIdAsync(state.ActorId.ToString());
                if (actor is null || actor.IsDisabled || !actor.TwoFactorEnabled || await users.IsLockedOutAsync(actor) || !await users.IsInRoleAsync(actor, "SuperAdmin")) throw new InvalidOperationException("sdk_fixture_superadmin_required");
                var apps = scope.ServiceProvider.GetRequiredService<IApplicationManagement>();
                var licenses = scope.ServiceProvider.GetRequiredService<ILicenseManagement>();
                var lifecycle = scope.ServiceProvider.GetRequiredService<ILicenseLifecycle>();
                var integration = scope.ServiceProvider.GetRequiredService<ILicenseIntegration>();
                var trace = "sdk-fixture:" + state.RunId.ToString("N");
                async Task Save()
                {
                    var temporary = path + ".tmp";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, Json), ct);
                    File.Move(temporary, path, true);
                }
                async Task<Guid> App(string name, string slug, Guid expected)
                {
                    var existing = await db.Set<ApplicationRow>().SingleOrDefaultAsync(x => x.Slug == slug, ct);
                    if (existing is not null)
                    {
                        if (existing.Name != name || !existing.IsActive || expected != Guid.Empty && existing.Id != expected) throw new InvalidOperationException("sdk_fixture_application_mismatch");
                        return existing.Id;
                    }
                    if (expected != Guid.Empty) throw new InvalidOperationException("sdk_fixture_application_missing");
                    return (await apps.CreateAsync(state.ActorId, Guid.NewGuid(), new(name, slug, true), trace, ct)).Id;
                }
                if (!transition)
                {
                    state.ApplicationId = await App("StockMini Integration Test", "stockmini-integration-test", state.ApplicationId); await Save();
                    state.OtherApplicationId = await App("LicenseHub Isolation Integration Test", "licensehub-isolation-integration-test", state.OtherApplicationId); await Save();
                    foreach (var application in new[] { state.ApplicationId, state.OtherApplicationId })
                    {
                        if (!await db.Set<ApplicationFeatureRow>().AnyAsync(x => x.ApplicationId == application && x.Code == state.Feature, ct))
                            await apps.SaveFeatureAsync(state.ActorId, application, null, Guid.NewGuid(), null, new(state.Feature, "SDK integration reports", true), trace, ct);
                        var settings = scope.ServiceProvider.GetRequiredService<INotificationManagement>();
                        var current = await settings.SettingsAsync(state.ActorId, application, ct);
                        if (current.Value.EmailEnabled)
                            await settings.SaveSettingsAsync(state.ActorId, application, Guid.NewGuid(), current.Etag, new([604800, 259200, 86400], [60], false, true), trace, ct);
                    }
                    var expiry = DateTimeOffset.UtcNow.AddDays(7);
                    async Task<CredentialIssued> Credential(Guid application, string[] scopes) => await apps.IssueAsync(state.ActorId, application, Guid.NewGuid(), new(scopes, expiry), trace, ct);
                    if (state.ClientSecret is null)
                    {
                        var credential = await Credential(state.ApplicationId, ["licenses:activate", "licenses:read", "warnings:read"]);
                        state.ClientSecret = credential.Secret; state.CredentialId = credential.Credential.Id; state.CredentialExpiresAt = credential.Credential.ExpiresAt; await Save();
                    }
                    if (state.OtherSecret is null) { var credential = await Credential(state.OtherApplicationId, ["licenses:activate", "licenses:read", "warnings:read"]); state.OtherSecret = credential.Secret; state.OtherCredentialId = credential.Credential.Id; await Save(); }
                    if (state.ReadOnlySecret is null) { var credential = await Credential(state.ApplicationId, ["licenses:read"]); state.ReadOnlySecret = credential.Secret; state.ReadOnlyCredentialId = credential.Credential.Id; await Save(); }
                    if (state.RevokedSecret is null)
                    {
                        var credential = await Credential(state.ApplicationId, ["licenses:read"]);
                        state.RevokedSecret = credential.Secret; state.RevokedCredentialId = credential.Credential.Id; await Save();
                    }
                    var revoked = await db.Set<ApplicationCredentialRow>().SingleAsync(x => x.Id == state.RevokedCredentialId && x.ApplicationId == state.ApplicationId, ct);
                    if (revoked.RevokedAt is null) await apps.RevokeAsync(state.ActorId, revoked.Id, Guid.NewGuid(), "\"" + revoked.Version + "\"", new("Dedicated SDK revoked-token fixture"), trace, ct);
                    foreach (var role in new[] { "activation", "expired", "renewed", "revoked", "lifetime", "suspended", "cancelled", "other", "other-exclusive" })
                    {
                        if (state.Cases.ContainsKey(role)) continue;
                        var application = role is "other" or "other-exclusive" ? state.OtherApplicationId : state.ApplicationId;
                        var subject = "sdk-it-" + state.RunId.ToString("N")[..12] + "-" + (role == "other" ? "activation" : role);
                        var customer = await db.Set<CustomerRow>().SingleOrDefaultAsync(x => x.ApplicationId == application && x.SubjectId == subject, ct);
                        var customerId = customer?.Id ?? (await licenses.CreateCustomerAsync(state.ActorId, Guid.NewGuid(), new(application, subject, "SDK fixture " + role, null), trace, ct)).Id;
                        var term = role == "lifetime" ? new TermWrite("Lifetime") : new TermWrite("Duration", new(role == "expired" ? "Minute" : "Hour", role == "expired" ? 2 : 24));
                        var issued = await licenses.IssueAsync(state.ActorId, Guid.NewGuid(), new(application, 1, "OnActivation", term, [state.Feature], customerId), trace, ct);
                        var item = issued.Items.Single();
                        state.Cases.Add(role, new() { ApplicationId = application, LicenseId = item.License.Id, CustomerId = customerId, SubjectId = subject, LicenseKey = item.LicenseKey!, ActivationOperation = Guid.NewGuid(), TransitionKey = Guid.NewGuid(), RenewalOperation = Guid.NewGuid() }); await Save();
                    }
                    state.Prepared = true; await Save();
                    Console.WriteLine($"SDK fixture preparada. ApplicationId: {state.ApplicationId}; OtherApplicationId: {state.OtherApplicationId}; CredentialId: {state.CredentialId}. Secretos solo en archivo privado.");
                }
                else
                {
                    if (!state.Prepared) throw new InvalidOperationException("sdk_fixture_not_prepared");
                    foreach (var role in new[] { "renewed", "revoked", "suspended", "cancelled" })
                    {
                        var fixture = state.Cases[role];
                        if (fixture.ApplicationId != state.ApplicationId) throw new InvalidOperationException("sdk_fixture_application_mismatch");
                        var license = await licenses.LicenseAsync(state.ActorId, fixture.LicenseId, ct);
                        if (license.ApplicationId != state.ApplicationId || license.CustomerId != fixture.CustomerId) throw new InvalidOperationException("sdk_fixture_resource_mismatch");
                        if (fixture.TransitionEtag is null)
                        {
                            if (license.State != "Active") throw new InvalidOperationException("sdk_fixture_activate_via_sdk_first");
                            fixture.TransitionEtag = license.Etag; fixture.BeforeExpiresAt = license.ExpiresAt;
                            fixture.BeforeVersion = (await integration.SnapshotAsync(state.ApplicationId, fixture.SubjectId, ct)).EntitlementVersion; await Save();
                        }
                        if (role == "renewed")
                        {
                            var request = new RenewalWrite(fixture.RenewalOperation, new("Duration", new("Day", 1)), false, "SDK real HTTPS renewal verification");
                            var result = await lifecycle.RenewAsync(state.ActorId, fixture.LicenseId, fixture.TransitionKey, fixture.TransitionEtag, request, trace, ct);
                            var replay = await lifecycle.RenewAsync(state.ActorId, fixture.LicenseId, fixture.TransitionKey, fixture.TransitionEtag, request, trace, ct);
                            if (result.ExpiresAt != replay.ExpiresAt || result.ExpiresAt != fixture.BeforeExpiresAt!.Value.AddDays(1)) throw new InvalidOperationException("sdk_fixture_renewal_mismatch");
                            fixture.AfterExpiresAt = result.ExpiresAt;
                            fixture.AfterVersion = (await integration.SnapshotAsync(state.ApplicationId, fixture.SubjectId, ct)).EntitlementVersion;
                        }
                        else await lifecycle.ChangeStateAsync(state.ActorId, fixture.LicenseId, fixture.TransitionKey, fixture.TransitionEtag,
                            role == "revoked" ? "revoke" : role == "suspended" ? "suspend" : "cancel", "Dedicated SDK transition fixture", trace, ct);
                        await Save();
                    }
                    state.TransitionsApplied = true; await Save();
                    Console.WriteLine("Transiciones de fixtures aplicadas y renovación idempotente verificada; consultar mediante SDK por HTTPS.");
                }
            }
            finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(hashtextextended('licensehub-sdk-integration-fixture', 0))", CancellationToken.None); }
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception is InvalidOperationException && exception.Message.StartsWith("sdk_fixture_", StringComparison.Ordinal) ? exception.Message : "sdk_fixture_failed");
            return 1;
        }
    }
}

// Plain classes intentionally avoid generated ToString methods containing secrets.
public sealed class SdkFixtureState
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ActorId { get; set; }
    public Guid RunId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OtherApplicationId { get; set; }
    public Guid CredentialId { get; set; }
    public Guid OtherCredentialId { get; set; }
    public Guid ReadOnlyCredentialId { get; set; }
    public Guid RevokedCredentialId { get; set; }
    public DateTime? CredentialExpiresAt { get; set; }
    public string BaseUrl { get; set; } = "https://localhost:8443/";
    public string Feature { get; set; } = "advanced_reports";
    public string? ClientSecret { get; set; }
    public string? OtherSecret { get; set; }
    public string? ReadOnlySecret { get; set; }
    public string? RevokedSecret { get; set; }
    public bool Prepared { get; set; }
    public bool TransitionsApplied { get; set; }
    public Dictionary<string, SdkFixtureLicense> Cases { get; set; } = [];
}
public sealed class SdkFixtureLicense
{
    public Guid ApplicationId { get; set; }
    public Guid LicenseId { get; set; }
    public Guid CustomerId { get; set; }
    public required string SubjectId { get; set; }
    public required string LicenseKey { get; set; }
    public Guid ActivationOperation { get; set; }
    public Guid TransitionKey { get; set; }
    public Guid RenewalOperation { get; set; }
    public string? TransitionEtag { get; set; }
    public DateTime? BeforeExpiresAt { get; set; }
    public DateTime? AfterExpiresAt { get; set; }
    public string? BeforeVersion { get; set; }
    public string? AfterVersion { get; set; }
}
