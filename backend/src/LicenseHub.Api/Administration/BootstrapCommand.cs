using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Administration;

public static class BootstrapCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            var path = configuration["Bootstrap:InputFile"];
            if (string.IsNullOrWhiteSpace(path) || new FileInfo(path).Length > 8192)
                throw new InvalidOperationException("bootstrap_private_file_required");
            await using var stream = File.OpenRead(path);
            var request = await JsonSerializer.DeserializeAsync<SuperAdminBootstrapRequest>(stream, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }, cancellationToken) ?? throw new InvalidOperationException("bootstrap_invalid_input");
            await using var scope = services.CreateAsyncScope();
            var id = await scope.ServiceProvider.GetRequiredService<ISuperAdminBootstrap>().InitializeAsync(request, cancellationToken);
            Console.WriteLine($"SuperAdmin creado con MFA habilitado. Id: {id}");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No imprimir JSON, contraseñas, claves ni excepciones de proveedores.
            var code = exception is InvalidOperationException && exception.Message.StartsWith("bootstrap_", StringComparison.Ordinal)
                ? exception.Message : "bootstrap_failed";
            Console.Error.WriteLine(code);
            return 1;
        }
    }
}
