using System.Text.Json.Serialization;

namespace LicenseHub.Application.Licensing;

public interface ICursorProtection
{
    string Protect(string value);
    string Unprotect(string value);
}

public interface ILicenseManagement
{
    Task<CustomerPage> CustomersAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct);
    Task<CustomerView> CreateCustomerAsync(Guid actor, Guid key, CustomerWrite request, string trace, CancellationToken ct);
    Task<CustomerView> UpdateCustomerAsync(Guid actor, Guid id, Guid key, string etag, CustomerContactWrite request, string trace, CancellationToken ct);
    Task<LicensePage> LicensesAsync(Guid actor, Guid applicationId, string? state, Guid? customerId, string? cursor, int pageSize, CancellationToken ct);
    Task<LicenseView> LicenseAsync(Guid actor, Guid id, CancellationToken ct);
    Task<IssueResponse> IssueAsync(Guid actor, Guid key, IssueRequest request, string trace, CancellationToken ct);
}

public interface ILicenseIntegration
{
    Task<ActivationResponse> ActivateAsync(Guid applicationId, Guid key, ActivationRequest request, string trace, CancellationToken ct);
    Task<EntitlementSnapshot> SnapshotAsync(Guid applicationId, string subjectId, CancellationToken ct);
}

public sealed record CustomerWrite([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] string SubjectId,
    [property: JsonRequired] string? DisplayName, [property: JsonRequired] string? Email);
public sealed record CustomerContactWrite([property: JsonRequired] string? DisplayName, [property: JsonRequired] string? Email);
public sealed record CustomerView(Guid Id, Guid ApplicationId, string SubjectId, string? DisplayName, string? Email, DateTime CreatedAt, string Etag);
public sealed record CustomerPage(CustomerView[] Items, string? NextCursor);
public sealed record DurationWrite([property: JsonRequired] string Unit, [property: JsonRequired] int Value);
[JsonConverter(typeof(TermWriteJsonConverter))]
public sealed record TermWrite(string Kind, DurationWrite? Duration = null, DateTimeOffset? Until = null);
public sealed record IssueRequest([property: JsonRequired] Guid ApplicationId, [property: JsonRequired] int Quantity,
    [property: JsonRequired] string StartMode, [property: JsonRequired] TermWrite Term, [property: JsonRequired] string[] Features, Guid? CustomerId = null);
public sealed record LicenseView(Guid Id, Guid ApplicationId, Guid? CustomerId, string State, string StartMode, bool IsLifetime,
    DateTime? StartsAt, DateTime? ActivatedAt, DateTime? ExpiresAt, string[] Features, DateTime CreatedAt, string Etag);
public sealed record LicensePage(LicenseView[] Items, string? NextCursor);
// Plain classes avoid generated ToString disclosing a license code.
public sealed class IssueItem
{
    public required LicenseView License { get; init; }
    public string? LicenseKey { get; init; }
}
public sealed class IssueResponse
{
    public bool CodesRecoverable { get; init; }
    public required IssueItem[] Items { get; init; }
}
public sealed class ActivationRequest
{
    public required string LicenseKey { get; init; }
    public required string SubjectId { get; init; }
}
public sealed record LicenseSummary(Guid Id, Guid CustomerId, bool IsLifetime, DateTime? ExpiresAt);
public sealed record EntitlementSnapshot(Guid ApplicationId, string SubjectId, LicenseSummary? License, string EffectiveState,
    bool HasPremium, string[] Features, DateTime EvaluatedAt, DateTime CacheUntil, string EntitlementVersion);
public sealed record ActivationResponse(Guid ActivationId, DateTime ActivatedAt, EntitlementSnapshot Snapshot);
