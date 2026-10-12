using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class LicenseRenewalRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public Guid LicenseId { get; set; }
    public Guid OperationId { get; set; }
    public Guid? PaymentId { get; set; }
    public DateTimeOffset? BeforeExpiresAt { get; set; }
    public DateTimeOffset? AfterExpiresAt { get; set; }
    public bool BeforeLifetime { get; set; }
    public bool AfterLifetime { get; set; }
    public bool Reactivated { get; set; }
    public Guid ActorId { get; set; }
    public required string Reason { get; set; }
}
