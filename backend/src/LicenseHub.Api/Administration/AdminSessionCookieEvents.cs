using System.Security.Claims;
using LicenseHub.Application.Administration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace LicenseHub.Api.Administration;

public sealed class AdminSessionCookieEvents(IAdminAuthentication authentication) : CookieAuthenticationEvents
{
    public const string SessionClaim = "licensehub:session";

    public static ClaimsPrincipal CreatePrincipal(AdminSessionGrant grant)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, grant.View.UserId.ToString()),
            new(ClaimTypes.Name, grant.View.Email),
            new(SessionClaim, grant.Id.ToString()), new("licensehub:mfa", "verified")
        };
        claims.AddRange(grant.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (Guid.TryParse(context.Principal?.FindFirstValue(SessionClaim), out var sessionId)
            && Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var grant = await authentication.ValidateAsync(sessionId, userId, context.HttpContext.RequestAborted);
            if (grant is not null)
            {
                // Roles actuales; no conserva permisos eliminados desde la emisión de la cookie.
                context.ReplacePrincipal(CreatePrincipal(grant));
                return;
            }
        }
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
