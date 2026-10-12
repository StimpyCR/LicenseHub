namespace LicenseHub.Application.Integration;

public interface IIntegrationAuthentication
{
    Task<IntegrationIdentity?> AuthenticateAsync(string secret, CancellationToken cancellationToken);
}

public sealed record IntegrationIdentity(Guid ApplicationId, Guid CredentialId, string[] Scopes);

public static class IntegrationScopes
{
    public const string Activate = "licenses:activate";
    public const string Read = "licenses:read";
    public const string Warnings = "warnings:read";
    public static bool IsValid(string scope) => scope is Activate or Read or Warnings;
}
