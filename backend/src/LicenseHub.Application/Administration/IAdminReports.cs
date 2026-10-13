namespace LicenseHub.Application.Administration;

public interface IAdminReports
{
    Task<DashboardView> DashboardAsync(Guid actor, Guid applicationId, CancellationToken ct);
    Task<AuditPage> AuditAsync(Guid actor, Guid applicationId, string? action, string? cursor, int size, CancellationToken ct);
    Task<byte[]> ExportAsync(Guid actor, Guid applicationId, string trace, CancellationToken ct);
}
public sealed record DashboardView(Guid ApplicationId, DateTime EvaluatedAt, int ActiveLicenses, int PendingLicenses, int ExpiringWithinSevenDays, int Customers);
public sealed record AuditView(Guid Id, Guid ApplicationId, string ActorId, string Action, Guid? EntityId, string? Reason, DateTime CreatedAt, string TraceId);
public sealed record AuditPage(AuditView[] Items, string? NextCursor);
