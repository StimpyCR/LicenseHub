using LicenseHub.Application.Availability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LicenseHub.Api.Health;

public sealed class StorageHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageAvailability>();
        try
        {
            return await storage.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Almacenamiento no disponible.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Almacenamiento no disponible.");
        }
    }
}
