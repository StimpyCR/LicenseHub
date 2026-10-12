using System.Text.Json.Serialization;

namespace LicenseHub.Application.Licensing;

public interface ILicenseLifecycle
{
    Task<LicenseView> ChangeStateAsync(Guid actor, Guid id, Guid key, string etag, string command, string reason, string trace, CancellationToken ct);
    Task<LicenseView> RenewAsync(Guid actor, Guid id, Guid key, string etag, RenewalWrite request, string trace, CancellationToken ct);
    Task<ActivationPage> ActivationsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct);
    Task<RenewalPage> RenewalsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct);
}

public sealed record RenewalWrite([property: JsonRequired] Guid OperationId, [property: JsonRequired] TermWrite Term,
    [property: JsonRequired] bool Reactivate, [property: JsonRequired] string Reason);
public sealed record ActivationRecord(Guid Id, Guid ApplicationId, Guid LicenseId, Guid CustomerId, string SubjectId, DateTime ActivatedAt);
public sealed record ActivationPage(ActivationRecord[] Items, string? NextCursor);
public sealed record RenewalRecord(Guid Id, Guid ApplicationId, Guid LicenseId, Guid OperationId, Guid? PaymentId,
    DateTime? BeforeExpiresAt, DateTime? AfterExpiresAt, bool BeforeLifetime, bool AfterLifetime, bool Reactivated,
    Guid ActorId, string Reason, DateTime CreatedAt);
public sealed record RenewalPage(RenewalRecord[] Items, string? NextCursor);
