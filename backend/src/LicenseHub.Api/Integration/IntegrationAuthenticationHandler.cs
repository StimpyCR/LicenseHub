using System.Security.Claims;
using System.Text.Encodings.Web;
using LicenseHub.Application.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LicenseHub.Api.Integration;

public sealed class IntegrationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IIntegrationAuthentication authentication) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationCredential";
    public const string ApplicationClaim = "licensehub:application";
    public const string ScopeClaim = "licensehub:scope";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count == 0) return AuthenticateResult.NoResult();
        if (header.Count != 1 || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(header[0], out var parsed)
            || !parsed.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || parsed.Parameter is null)
            return AuthenticateResult.Fail("Credencial inválida.");
        var identity = await authentication.AuthenticateAsync(parsed.Parameter, Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("Credencial inválida.");
        var claims = new List<Claim>
        {
            new(ApplicationClaim, identity.ApplicationId.ToString()),
            new(ClaimTypes.NameIdentifier, identity.CredentialId.ToString())
        };
        claims.AddRange(identity.Scopes.Select(scope => new Claim(ScopeClaim, scope)));
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: 401, title: "Autenticación requerida",
            extensions: new Dictionary<string, object?> { ["code"] = "authentication_required" }).ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Results.Problem(statusCode: 403, title: "Permiso insuficiente",
            extensions: new Dictionary<string, object?> { ["code"] = "forbidden" }).ExecuteAsync(Context);
}
