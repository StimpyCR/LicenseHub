namespace LicenseHub.Infrastructure.Persistence.Records;

public interface ITenantRow
{
    Guid Id { get; set; }
    Guid ApplicationId { get; set; }
    long Version { get; set; }
}
