using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class IdempotencyRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public required string PrincipalId { get; set; }
    public required string Operation { get; set; }
    public Guid Key { get; set; }
    public required string RequestHash { get; set; }
    public string? ResponseJson { get; set; }
    public int? ResponseStatus { get; set; }
    public required string State { get; set; }
    public DateTimeOffset RetainUntil { get; set; }
}
