using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.SDK.Abstractions;
using LicenseHub.SDK.Exceptions;
using LicenseHub.SDK.Extensions;
using LicenseHub.SDK.Models;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3 || args[0] is not ("before" or "after") || Environment.GetEnvironmentVariable("LICENSEHUB_LIVE_TESTS") != "1"
    || Environment.GetEnvironmentVariable("LICENSEHUB_ALLOW_TEST_ACTIVATION") != "1")
{ Console.Error.WriteLine("Explicit live-fixture opt-in and stage/state/output paths are required."); return 1; }
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var file = JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(args[1]), json)!;
var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
var checks = new List<string>(); var wire = new List<object>();
using var raw = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new(file.BaseUrl), Timeout = TimeSpan.FromSeconds(20) };
ServiceProvider Client(Guid application, string credential)
{
    var services = new ServiceCollection(); services.AddSingleton<ILicenseHubCredentialProvider>(new Credential(credential));
    services.AddLicenseHub(options => { options.BaseUrl = file.BaseUrl; options.ApplicationId = application; options.CacheTtl = TimeSpan.Zero; });
    return services.BuildServiceProvider();
}
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS " + name); }
async Task<JsonElement> Wire(string path, string token, int expected, object? body = null, Guid? operation = null)
{
    using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (body is not null) request.Content = JsonContent.Create(body);
    if (operation is not null) request.Headers.Add("Idempotency-Key", operation.Value.ToString());
    using var response = await raw.SendAsync(request); Check((int)response.StatusCode == expected, "HTTP " + expected + " " + path.Split('?')[0]);
    var payload = await response.Content.ReadAsStringAsync(); var element = JsonSerializer.Deserialize<JsonElement>(payload);
    wire.Add(new { path = path.Split('?')[0], method = body is null ? "get" : "post", status = (int)response.StatusCode, media = response.Content.Headers.ContentType!.MediaType, response = element });
    Check(response.Headers.CacheControl?.NoStore == true, "no-store " + path.Split('?')[0]);
    return element;
}
try
{
    using var primary = Client(file.ApplicationId, file.ClientSecret); var sdk = primary.GetRequiredService<ILicenseHubClient>();
    if (args[0] == "before")
    {
        var absent = await sdk.GetStatusAsync("sdk-it-" + file.RunId.ToString("N")[..12] + "-absent");
        Check(absent.EffectiveState == EffectiveState.None && absent.License is null && !absent.HasPremium, "None is a successful denied snapshot");
        await Wire("/api/v1/licenses/status?subjectId=" + absent.SubjectId, file.ClientSecret, 200);
        var other = file.Cases["other"];
        try { await sdk.ActivateAsync(new() { LicenseKey = other.LicenseKey, SubjectId = other.SubjectId }, Guid.NewGuid()); throw new InvalidOperationException("foreign key accepted"); }
        catch (LicenseHubException exception) { Check(exception.StatusCode == 404, "Foreign application key is hidden with 404"); }
        using var readOnly = Client(file.ApplicationId, file.ReadOnlySecret);
        try { await readOnly.GetRequiredService<ILicenseHubClient>().GetWarningsAsync(absent.SubjectId); throw new InvalidOperationException("missing scope accepted"); }
        catch (LicenseHubAuthorizationException exception) { Check(exception.StatusCode == 403, "Scope warnings:read is required"); }
        try { await readOnly.GetRequiredService<ILicenseHubClient>().ActivateAsync(new() { LicenseKey = file.Cases["activation"].LicenseKey, SubjectId = file.Cases["activation"].SubjectId }, Guid.NewGuid()); throw new InvalidOperationException("missing scope accepted"); }
        catch (LicenseHubAuthorizationException exception) { Check(exception.StatusCode == 403, "Scope licenses:activate is required"); }
        foreach (var pair in file.Cases)
        {
            var fixture = pair.Value;
            var token = fixture.ApplicationId == file.OtherApplicationId ? file.OtherSecret : file.ClientSecret;
            using var container = Client(fixture.ApplicationId, token);
            var client = container.GetRequiredService<ILicenseHubClient>();
            var request = new ActivateLicenseRequest { LicenseKey = fixture.LicenseKey, SubjectId = fixture.SubjectId };
            var first = await client.ActivateAsync(request, fixture.ActivationOperation);
            var replay = await client.ActivateAsync(request, fixture.ActivationOperation);
            Check(first.ActivationId == replay.ActivationId && first.Snapshot.License!.Id == fixture.LicenseId, "Activation and exact replay " + pair.Key);
            await Wire("/api/v1/licenses/activate", token, 200, request, fixture.ActivationOperation);
            var status = await client.GetStatusAsync(fixture.SubjectId);
            Check(status.HasPremium && status.EffectiveState == EffectiveState.Active && status.License!.Id == fixture.LicenseId, "Active snapshot " + pair.Key);
            Check(await client.HasFeatureAsync(fixture.SubjectId, file.Feature), "Feature granted " + pair.Key);
            Check(!await client.HasFeatureAsync(fixture.SubjectId, "missing_feature"), "Unknown feature denied " + pair.Key);
            if (pair.Key == "lifetime") Check(status.License!.IsLifetime && status.License.ExpiresAt is null, "Lifetime null semantics");
            await Wire("/api/v1/licenses/status?subjectId=" + fixture.SubjectId, token, 200);
        }
        var core = file.Cases["activation"];
        try { await sdk.ActivateAsync(new() { LicenseKey = core.LicenseKey, SubjectId = core.SubjectId + "-different" }, core.ActivationOperation); throw new InvalidOperationException("changed payload accepted"); }
        catch (LicenseHubConflictException exception) { Check(exception.Code == "idempotency_conflict", "Same idempotency key with changed payload is 409"); }
        try { await sdk.ActivateAsync(new() { LicenseKey = core.LicenseKey, SubjectId = core.SubjectId }, Guid.NewGuid()); throw new InvalidOperationException("second redemption accepted"); }
        catch (LicenseHubKeyUsedException exception) { Check(exception.Code == "license_already_activated", "Second redemption with new operation is 409"); }
        var scoped = await Wire("/api/v1/licenses/status?subjectId=" + core.SubjectId + "&applicationId=" + file.OtherApplicationId, file.ClientSecret, 200);
        Check(scoped.GetProperty("applicationId").GetGuid() == file.ApplicationId && scoped.GetProperty("license").GetProperty("id").GetGuid() == core.LicenseId, "Application comes from opaque token despite forged query");
        var features = await Wire("/api/v1/licenses/features?subjectId=" + core.SubjectId, file.ClientSecret, 200);
        Check(features.GetProperty("features").EnumerateArray().Any(x => x.GetString() == file.Feature), "Features endpoint matches SDK rights");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await sdk.GetStatusAsync(core.SubjectId, cancelled.Token); throw new InvalidOperationException("cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "SDK CancellationToken propagated"); }
        var expiry = file.Cases["expired"];
        var initial = await sdk.GetStatusAsync(expiry.SubjectId);
        var waitUntil = initial.License!.ExpiresAt!.Value.AddSeconds(1);
        Console.WriteLine("Waiting for the dedicated two-minute license to expire naturally; no database clock changes.");
        while (DateTimeOffset.UtcNow < waitUntil) await Task.Delay(TimeSpan.FromSeconds(Math.Min(20, Math.Max(1, (waitUntil - DateTimeOffset.UtcNow).TotalSeconds))));
        var expired = await sdk.GetStatusAsync(expiry.SubjectId);
        Check(expired.EffectiveState == EffectiveState.Expired && !expired.HasPremium && expired.Features.Count == 0, "Natural two-minute expiration denies rights");
        await Wire("/api/v1/licenses/status?subjectId=" + expiry.SubjectId, file.ClientSecret, 200);
    }
    else
    {
        if (!file.TransitionsApplied) throw new InvalidOperationException("Administrative transitions have not run.");
        foreach (var pair in new[] { ("revoked", EffectiveState.Revoked), ("suspended", EffectiveState.Suspended), ("cancelled", EffectiveState.Cancelled), ("expired", EffectiveState.Expired) })
        {
            var fixture = file.Cases[pair.Item1]; var current = await sdk.GetStatusAsync(fixture.SubjectId);
            Check(current.EffectiveState == pair.Item2 && !current.HasPremium && current.Features.Count == 0, "Current denied state " + pair.Item1);
            Check(!await sdk.HasFeatureAsync(fixture.SubjectId, file.Feature), "Denied feature " + pair.Item1);
            await Wire("/api/v1/licenses/status?subjectId=" + fixture.SubjectId, file.ClientSecret, 200);
        }
        var renewed = file.Cases["renewed"]; var status = await sdk.GetStatusAsync(renewed.SubjectId);
        Check(status.HasPremium && status.License!.ExpiresAt == renewed.AfterExpiresAt && status.License.ExpiresAt == renewed.BeforeExpiresAt!.Value.AddDays(1) && status.EntitlementVersion == renewed.AfterVersion && status.EntitlementVersion != renewed.BeforeVersion, "Renewal extends exactly once and updates version");
        await Wire("/api/v1/licenses/status?subjectId=" + renewed.SubjectId, file.ClientSecret, 200);
        var revokedFixture = file.Cases["revoked"];
        var historical = await sdk.ActivateAsync(new() { LicenseKey = revokedFixture.LicenseKey, SubjectId = revokedFixture.SubjectId }, revokedFixture.ActivationOperation);
        Check(historical.Snapshot.HasPremium && !await sdk.HasFeatureAsync(revokedFixture.SubjectId, file.Feature), "Historical activation replay never grants current rights");
        using var revoked = Client(file.ApplicationId, file.RevokedSecret);
        try { await revoked.GetRequiredService<ILicenseHubClient>().GetStatusAsync(renewed.SubjectId); throw new InvalidOperationException("revoked credential accepted"); }
        catch (LicenseHubAuthenticationException exception) { Check(exception.StatusCode == 401, "Revoked opaque credential returns typed 401"); }
        await Wire("/api/v1/licenses/status?subjectId=" + renewed.SubjectId, file.RevokedSecret, 401);
        var warnings = await sdk.GetWarningsAsync(renewed.SubjectId, pageSize: 1);
        Check(warnings.Items.All(x => x.LicenseId == renewed.LicenseId), "Warnings belong to current license");
        await Wire("/api/v1/licenses/warnings?subjectId=" + renewed.SubjectId + "&pageSize=1", file.ClientSecret, 200);
        var coreFixture = file.Cases["activation"];
        var coreWarnings = await sdk.GetWarningsAsync(coreFixture.SubjectId, pageSize: 1);
        Check(coreWarnings.Items.Count > 0 && coreWarnings.Items.All(x => x.LicenseId == coreFixture.LicenseId), "Real warning DTO and severity deserialize in SDK");
        await Wire("/api/v1/licenses/warnings?subjectId=" + coreFixture.SubjectId + "&pageSize=1", file.ClientSecret, 200);
        using var other = Client(file.OtherApplicationId, file.OtherSecret);
        var exclusive = file.Cases["other-exclusive"];
        var otherClient = other.GetRequiredService<ILicenseHubClient>();
        var exclusiveRequest = new ActivateLicenseRequest { LicenseKey = exclusive.LicenseKey, SubjectId = exclusive.SubjectId };
        await otherClient.ActivateAsync(exclusiveRequest, exclusive.ActivationOperation);
        Check(await otherClient.HasFeatureAsync(exclusive.SubjectId, file.Feature) && !await sdk.HasFeatureAsync(exclusive.SubjectId, file.Feature), "Business exclusive to application B cannot grant application A");
        await Wire("/api/v1/licenses/activate", file.OtherSecret, 200, exclusiveRequest, exclusive.ActivationOperation);
        var otherStatus = await other.GetRequiredService<ILicenseHubClient>().GetStatusAsync(file.Cases["activation"].SubjectId);
        var mainStatus = await sdk.GetStatusAsync(file.Cases["activation"].SubjectId);
        Check(otherStatus.License!.Id == file.Cases["other"].LicenseId && mainStatus.License!.Id == file.Cases["activation"].LicenseId && otherStatus.License.Id != mainStatus.License.Id, "Same subject is isolated in two real applications");
    }
    await File.WriteAllTextAsync(Path.Combine(output, args[0] + "-wire.json"), JsonSerializer.Serialize(wire, json));
    await File.WriteAllTextAsync(Path.Combine(output, args[0] + "-result.json"), JsonSerializer.Serialize(new { stage = args[0], checks = checks.Count, passed = true, applicationId = file.ApplicationId, runId = file.RunId, evaluatedAt = DateTime.UtcNow, sdkVersion = typeof(ILicenseHubClient).Assembly.GetName().Version!.ToString() }, json));
    Console.WriteLine($"Stage {args[0]} passed: {checks.Count} checks. Evidence excludes headers and secret request bodies."); return 0;
}
catch (Exception exception)
{
    // Do not print exception messages: remote responses and providers are untrusted.
    Console.Error.WriteLine("Compatibility stage failed: " + exception.GetType().Name); return 1;
}
sealed class Credential(string secret) : ILicenseHubCredentialProvider
{
    public ValueTask<string> GetCredentialAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(secret); }
}
sealed class Fixture
{
    public required string BaseUrl { get; init; }
    public Guid RunId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid OtherApplicationId { get; init; }
    public required string ClientSecret { get; init; }
    public required string OtherSecret { get; init; }
    public required string ReadOnlySecret { get; init; }
    public required string RevokedSecret { get; init; }
    public required string Feature { get; init; }
    public bool TransitionsApplied { get; init; }
    public required Dictionary<string,FixtureLicense> Cases { get; init; }
}
sealed class FixtureLicense
{
    public Guid ApplicationId { get; init; }
    public Guid LicenseId { get; init; }
    public required string SubjectId { get; init; }
    public required string LicenseKey { get; init; }
    public Guid ActivationOperation { get; init; }
    public DateTimeOffset? BeforeExpiresAt { get; init; }
    public DateTimeOffset? AfterExpiresAt { get; init; }
    public string? BeforeVersion { get; init; }
    public string? AfterVersion { get; init; }
}
