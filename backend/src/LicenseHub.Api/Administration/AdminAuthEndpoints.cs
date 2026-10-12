using System.Security.Claims;
using LicenseHub.Application.Administration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace LicenseHub.Api.Administration;

public static class AdminAuthEndpoints
{
    public const string ChallengeScheme = "LicenseHub.AdminMfaChallenge";

    public static void MapAdminAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/auth").AddEndpointFilter<AdminAuthErrorFilter>();
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        group.MapPost("/login", async (AdminLoginRequest request, IAdminAuthentication authentication, HttpContext context) =>
        {
            var challenge = await authentication.BeginAsync(request, context.TraceIdentifier, context.RequestAborted);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, challenge.Id.ToString())], ChallengeScheme));
            await context.SignInAsync(ChallengeScheme, principal, new AuthenticationProperties
            {
                ExpiresUtc = challenge.ExpiresAt,
                IsPersistent = false,
                AllowRefresh = false
            });
            return Results.Json(new { mfaRequired = true, challengeExpiresAt = challenge.ExpiresAt.UtcDateTime }, statusCode: 202);
        }).AllowAnonymous().RequireRateLimiting("AdminLogin").AddEndpointFilter<AdminCsrfFilter>();
        group.MapPost("/mfa", async (AdminMfaRequest request, IAdminAuthentication authentication, HttpContext context) =>
        {
            var challengeCookie = await context.AuthenticateAsync(ChallengeScheme);
            if (!challengeCookie.Succeeded || !Guid.TryParse(challengeCookie.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var challengeId))
                throw new AdminAuthenticationException("authentication_required", 401);
            var grant = await authentication.CompleteAsync(challengeId, request, context.TraceIdentifier, context.RequestAborted);
            await context.SignInAsync(IdentityConstants.ApplicationScheme, AdminSessionCookieEvents.CreatePrincipal(grant), new AuthenticationProperties
            {
                ExpiresUtc = grant.ExpiresAt,
                IsPersistent = false,
                AllowRefresh = false
            });
            await context.SignOutAsync(ChallengeScheme);
            return Results.Ok(grant.View);
        }).AllowAnonymous().RequireRateLimiting("AdminMfa").AddEndpointFilter<AdminCsrfFilter>();
        group.MapGet("/session", async (IAdminAuthentication authentication, HttpContext context) =>
        {
            var grant = await authentication.ValidateAsync(SessionId(context), UserId(context), context.RequestAborted);
            return grant is null ? Problem("authentication_required", 401) : Results.Ok(grant.View);
        }).RequireAuthorization("AdminSession");
        group.MapPost("/logout", async (IAdminAuthentication authentication, HttpContext context) =>
        {
            await authentication.RevokeAsync(SessionId(context), UserId(context), context.TraceIdentifier, context.RequestAborted);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            await context.SignOutAsync(ChallengeScheme);
            return Results.NoContent();
        }).RequireAuthorization("AdminSession").AddEndpointFilter<AdminCsrfFilter>();
    }

    private static Guid SessionId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(AdminSessionCookieEvents.SessionClaim)!);
    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static IResult Problem(string code, int status) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed class AdminAuthErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (AdminAuthenticationException exception) { return AdminAuthEndpoints.Problem(exception.Code, exception.StatusCode); }
    }
}

public sealed class AdminCsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) { return AdminAuthEndpoints.Problem("forbidden", 403); }
        return await next(context);
    }
}
