namespace LicenseHub.Application.Administration;

public interface IAuthMaintenance
{
    Task CleanupAsync(CancellationToken cancellationToken);
}
