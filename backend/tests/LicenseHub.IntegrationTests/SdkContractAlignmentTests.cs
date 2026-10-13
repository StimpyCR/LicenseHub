using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LicenseHub.Api.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace LicenseHub.IntegrationTests;

public sealed class SdkContractAlignmentTests
{
    [Fact]
    public async Task ActivationRejectsExtraIdentityFieldsAndDuplicateJsonProperties()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var issued = await f.Issue();
        var secret = await f.Credential(["licenses:activate"]);
        using var client = f.Fixture.Factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var key = JsonSerializer.Serialize(issued.Items[0].LicenseKey);
        foreach (var body in new[] {
            "{\"licenseKey\":" + key + ",\"subjectId\":\"sdk-business\",\"applicationId\":\"" + f.App + "\"}",
            "{\"licenseKey\":" + key + ",\"subjectId\":\"sdk-business\",\"customerId\":\"" + Guid.NewGuid() + "\"}",
            "{\"licenseKey\":" + key + ",\"subjectId\":\"sdk-business\",\"subjectId\":\"other-business\"}" })
        {
            using var response = await client.PostAsync("/api/v1/licenses/activate", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal("Pending", (await f.Admin(s => s.LicenseAsync(f.Actor, issued.Items[0].License.Id, default))).State);
    }

    [Fact]
    public async Task PrivateFixturePreparationReusesApplicationsAndDoesNotResetUsers()
    {
        await using var f = await LicenseOperationsTests.SetupAsync();
        var directory = Directory.CreateTempSubdirectory("licensehub-sdk-fixture-");
        var path = Path.Combine(directory.FullName, "state.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SdkFixtureState { ActorId = f.Actor, RunId = Guid.NewGuid() }, JsonSerializerOptions.Web));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SdkFixture:StateFile"] = path }).Build();
            var services = f.Fixture.Factory.Services;
            var environment = services.GetRequiredService<IHostEnvironment>();
            Assert.Equal(0, await SdkFixtureCommand.RunAsync(services, configuration, environment, false, default));
            var first = JsonSerializer.Deserialize<SdkFixtureState>(await File.ReadAllTextAsync(path), JsonSerializerOptions.Web)!;
            Assert.Equal(9, first.Cases.Count);
            Assert.Equal(0, await SdkFixtureCommand.RunAsync(services, configuration, environment, false, default));
            var replay = JsonSerializer.Deserialize<SdkFixtureState>(await File.ReadAllTextAsync(path), JsonSerializerOptions.Web)!;
            Assert.Equal(first.ApplicationId, replay.ApplicationId);
            Assert.Equal(first.CredentialId, replay.CredentialId);
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>();
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(4, await db.Set<ApplicationCredentialRow>().CountAsync());
            Assert.Equal(9, await db.Set<LicenseRow>().CountAsync());
            Assert.All(await db.Set<ApplicationCredentialRow>().ToArrayAsync(), row => Assert.NotEqual(Guid.Empty, row.ApplicationId));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); directory.Delete(); }
    }
}
