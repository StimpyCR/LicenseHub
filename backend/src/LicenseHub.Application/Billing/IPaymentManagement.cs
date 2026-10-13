using System.Text.Json.Serialization;
using LicenseHub.Application.Licensing;

namespace LicenseHub.Application.Billing;

public interface IPaymentManagement
{
    Task<PaymentPage> ListAsync(Guid actor, Guid applicationId, string? state, string? cursor, int pageSize, CancellationToken ct);
    Task<PaymentView> CreateAsync(Guid actor, Guid key, PaymentWrite request, string trace, CancellationToken ct);
    Task<PaymentView> ChangeAsync(Guid actor, Guid id, Guid key, string etag, string command, string reason, string trace, CancellationToken ct);
}
public sealed record PaymentWrite([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] Guid LicenseId,
    [property: JsonRequired] Guid CustomerId, [property: JsonRequired] string Amount, [property: JsonRequired] string Currency,
    [property: JsonRequired] string Reference, [property: JsonRequired] RenewalWrite? RenewalIntent);
public sealed record PaymentView(Guid Id, Guid ApplicationId, Guid LicenseId, Guid CustomerId, string Amount,
    string Currency, string State, string Reference, DateTime CreatedAt, string Etag, RenewalWrite? RenewalIntent, DateTime? ConfirmedAt);
public sealed record PaymentPage(PaymentView[] Items, string? NextCursor);
