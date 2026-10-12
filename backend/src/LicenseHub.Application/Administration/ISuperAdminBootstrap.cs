namespace LicenseHub.Application.Administration;

public interface ISuperAdminBootstrap
{
    Task<Guid> InitializeAsync(SuperAdminBootstrapRequest request, CancellationToken cancellationToken);
}

// No record: ToString no debe incorporar secretos del operador.
public sealed class SuperAdminBootstrapRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string AuthenticatorKey { get; init; }
    public required string Code { get; init; }
}
