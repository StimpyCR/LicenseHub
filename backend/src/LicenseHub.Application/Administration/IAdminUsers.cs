namespace LicenseHub.Application.Administration;

public interface IAdminUsers
{
    Task<AdminUserPage> ListAsync(Guid actor, string? cursor, int size, CancellationToken ct);
    Task<AdminUserProfile> CreateAsync(Guid actor, Guid key, AdminUserCreate request, string trace, CancellationToken ct);
    Task<AdminUserProfile> UpdateAsync(Guid actor, Guid id, string etag, AdminUserAccessWrite request, string trace, CancellationToken ct);
}
public interface IAdminEnrollment
{
    Task EnrollAsync(AdminRecoveryRequest request, string operatorId, CancellationToken cancellationToken);
}
public sealed record AdminUserCreate(string Email);
public sealed record AdminUserGrant(Guid ApplicationId, string Capability);
public sealed record AdminUserAccessWrite(bool IsDisabled, AdminUserGrant[] Grants, string Reason);
public sealed record AdminUserProfile(Guid Id, string Email, bool IsDisabled, bool MfaEnrolled, bool IsSuperAdmin, string Etag, AdminUserGrant[] Grants);
public sealed record AdminUserPage(AdminUserProfile[] Items, string? NextCursor);
