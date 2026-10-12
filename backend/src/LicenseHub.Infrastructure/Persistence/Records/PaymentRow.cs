using LicenseHub.Domain.Licenses;

namespace LicenseHub.Infrastructure.Persistence.Records;

public sealed class PaymentRow : ITenantRow
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
    public Guid LicenseId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string State { get; set; }
    public required string Reference { get; set; }
    public string? RenewalIntentJson { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
