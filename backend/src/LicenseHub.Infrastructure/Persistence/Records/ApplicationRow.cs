using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class ApplicationRow
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; }
}
