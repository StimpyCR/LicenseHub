namespace LicenseHub.Application.Availability;

public interface IStorageAvailability
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
