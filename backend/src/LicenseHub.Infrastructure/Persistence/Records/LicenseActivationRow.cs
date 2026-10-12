using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class LicenseActivationRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public Guid LicenseId { get; set; }
    public Guid CustomerId { get; set; }
    public required string SubjectId { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
}
