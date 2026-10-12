using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Administration;

public static class RecoveryCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct, bool enrollment = false)
    {
        try
        {
            var path = configuration[enrollment ? "Enrollment:InputFile" : "Recovery:InputFile"];
            if (string.IsNullOrWhiteSpace(path) || new FileInfo(path).Length > 8192)
                throw new InvalidOperationException("recovery_private_file_required");
            await using var stream = File.OpenRead(path);
            var request = await JsonSerializer.DeserializeAsync<AdminRecoveryRequest>(stream, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }, ct) ?? throw new InvalidOperationException("recovery_invalid_input");
            await using var scope = services.CreateAsyncScope();
            if (enrollment) await scope.ServiceProvider.GetRequiredService<IAdminEnrollment>().EnrollAsync(request, "operator:" + Environment.UserName, ct);
            else await scope.ServiceProvider.GetRequiredService<IAdminRecovery>().RecoverAsync(request, "operator:" + Environment.UserName, ct);
            Console.WriteLine(enrollment ? "Administrador delegado enrolado con MFA." : "Administrador recuperado con MFA. Sesiones anteriores invalidadas.");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception is InvalidOperationException && exception.Message.StartsWith("recovery_", StringComparison.Ordinal)
                ? exception.Message : "recovery_failed");
            return 1;
        }
    }
}
