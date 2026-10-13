using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LicenseHub.SecurityTests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public async Task ProductionLoadsPrivateCertificatePasswordFileAndEncryptsPersistentKeys()
    {
        var directory = Directory.CreateTempSubdirectory("licensehub-production-tests-").FullName;
        try
        {
            var keyDirectory = Path.Combine(directory, "keys");
            var certificatePath = Path.Combine(directory, "protection.pfx");
            var passwordPath = Path.Combine(directory, "password");
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LicenseHub test key encryption", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, password));
            await File.WriteAllTextAsync(passwordPath, password);
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:LicenseHub", "Host=127.0.0.1;Database=unavailable;Username=test;Timeout=1");
                builder.UseSetting("Jobs:Enabled", "false");
                builder.UseSetting("AllowedHosts", "localhost");
                builder.UseSetting("DataProtection:KeyPath", keyDirectory);
                builder.UseSetting("DataProtection:CertificatePath", certificatePath);
                builder.UseSetting("DataProtection:CertificatePasswordFile", passwordPath);
            });
            using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/auth/csrf")).StatusCode);
            var files = Directory.GetFiles(keyDirectory, "key-*.xml");
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var xml = await File.ReadAllTextAsync(file);
                Assert.Contains("encryptedSecret", xml);
                Assert.DoesNotContain("<masterKey", xml);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("AllowedHosts", "*", "dominios explícitos")]
    [InlineData("DataProtection:CertificatePath", "", "certificado de cifrado")]
    [InlineData("Smtp:AllowUnencryptedDevelopment", "true", "exclusivamente en desarrollo")]
    public void UnsafeProductionConfigurationFailsBeforeServingTraffic(string setting, string value, string diagnostic)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:LicenseHub", "Host=127.0.0.1;Database=unavailable;Username=test;Timeout=1");
            builder.UseSetting("Jobs:Enabled", "false");
            builder.UseSetting("AllowedHosts", "licensehub.example.invalid");
            builder.UseSetting("DataProtection:KeyPath", Path.GetTempPath());
            builder.UseSetting(setting, value);
        });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(diagnostic, exception.Message);
    }
}
