using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class CustomerRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string SubjectId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}
