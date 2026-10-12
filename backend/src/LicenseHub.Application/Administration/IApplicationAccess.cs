namespace LicenseHub.Application.Administration;

public interface IApplicationAccess
{
    Task<bool> IsAllowedAsync(Guid userId, Guid applicationId, string capability, CancellationToken cancellationToken);
}
