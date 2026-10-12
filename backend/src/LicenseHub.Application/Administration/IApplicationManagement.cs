namespace LicenseHub.Application.Administration;

public interface IApplicationManagement
{
    Task<ApplicationPage> ListAsync(Guid actor, string? cursor, int pageSize, CancellationToken ct);
    Task<ApplicationView> CreateAsync(Guid actor, Guid key, ApplicationWrite request, string trace, CancellationToken ct);
    Task<ApplicationView> UpdateAsync(Guid actor, Guid id, Guid key, string etag, ApplicationWrite request, string trace, CancellationToken ct);
    Task<CredentialPage> CredentialsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct);
    Task<CredentialIssued> IssueAsync(Guid actor, Guid id, Guid key, CredentialWrite request, string trace, CancellationToken ct);
    Task<CredentialView> RevokeAsync(Guid actor, Guid id, Guid key, string etag, StateChangeWrite request, string trace, CancellationToken ct);
}

public sealed record ApplicationWrite(string Name, string Slug, bool IsActive);
public sealed record ApplicationView(Guid Id, string Name, string Slug, bool IsActive, DateTime CreatedAt, string Etag);
public sealed record ApplicationPage(ApplicationView[] Items, string? NextCursor);
public sealed record CredentialWrite(string[] Scopes, DateTimeOffset ExpiresAt);
public sealed record StateChangeWrite(string Reason);
public sealed record CredentialView(Guid Id, Guid ApplicationId, string[] Scopes, DateTime CreatedAt, DateTime ExpiresAt, DateTime? RevokedAt, string Etag);
public sealed record CredentialPage(CredentialView[] Items, string? NextCursor);
// Class deliberately avoids a generated ToString containing the secret.
public sealed class CredentialIssued
{
    public required CredentialView Credential { get; init; }
    public required string Secret { get; init; }
}
public sealed class ManagementException(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
