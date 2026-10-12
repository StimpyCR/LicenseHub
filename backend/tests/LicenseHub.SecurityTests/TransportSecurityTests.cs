using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LicenseHub.SecurityTests;

public sealed class TransportSecurityTests
{
    [Fact]
    public async Task LoginRateLimitsRequestsBeforeDatabaseAccess()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "rate-test@example.invalid", password = "invalid" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var limited = await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "rate-test@example.invalid", password = "invalid" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        Assert.Contains("rate_limited", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RejectsUnexpectedJsonFields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "test@example.invalid", password = "invalid", role = "SuperAdmin" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:LicenseHub", "Host=127.0.0.1;Database=unavailable;Username=test;Timeout=1");
            builder.UseSetting("Jobs:Enabled", "false");
        });

    [Fact]
    public async Task RejectsCleartextBusinessRequests()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/v1/licenses/status?subjectId=business-42");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("https_required", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task DoesNotTrustForwardedProtoFromUnconfiguredPeer()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var response = await client.GetAsync("/api/v1/licenses/status?subjectId=business-42");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DoesNotExposeHangfireDashboard()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await client.GetAsync("/hangfire");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task LivenessDoesNotDependOnDatabaseOrRevealSecrets()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
