namespace LicenseHub.Application.Administration;

public interface IAdminRecovery
{
    Task RecoverAsync(AdminRecoveryRequest request, string operatorId, CancellationToken cancellationToken);
}

public sealed class AdminRecoveryRequest
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string Confirmation { get; init; }
    public required string Reason { get; init; }
    public required string Password { get; init; }
    public required string AuthenticatorKey { get; init; }
    public required string Code { get; init; }
}
