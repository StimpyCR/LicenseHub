namespace LicenseHub.Application.Administration;

public interface IAdminAuthentication
{
    Task<AdminChallenge> BeginAsync(AdminLoginRequest request, string correlationId, CancellationToken cancellationToken);
    Task<AdminSessionGrant> CompleteAsync(Guid challengeId, AdminMfaRequest request, string correlationId, CancellationToken cancellationToken);
    Task<AdminSessionGrant?> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);
    Task RevokeAsync(Guid sessionId, Guid userId, string correlationId, CancellationToken cancellationToken);
}

public sealed class AdminLoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class AdminMfaRequest
{
    public required string Code { get; init; }
}

public sealed record AdminChallenge(Guid Id, DateTimeOffset ExpiresAt);
public sealed record AdminSessionView(Guid UserId, string Email, bool MfaVerified, string[] Capabilities, Guid[] ApplicationIds);
public sealed record AdminSessionGrant(Guid Id, DateTimeOffset ExpiresAt, AdminSessionView View, string[] Roles);

public sealed class AdminAuthenticationException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
