using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class ApplicationFeatureRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
}
