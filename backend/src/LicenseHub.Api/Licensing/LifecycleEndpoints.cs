using System.Security.Claims;
using LicenseHub.Api.Administration;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;

namespace LicenseHub.Api.Licensing;

public static class LifecycleEndpoints
{
    public static void MapLicenseLifecycle(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/licenses").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        group.MapPost("/{id:guid}/renew", async (Guid id, RenewalWrite request, ILicenseLifecycle service, HttpContext context) =>
        {
            var result = await service.RenewAsync(Actor(context), id, Key(context), Match(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Ok(result);
        }).AddEndpointFilter<AdminCsrfFilter>();
        foreach (var command in new[] { "suspend", "revoke", "cancel", "reactivate" })
        {
            group.MapPost("/{id:guid}/" + command, async (Guid id, StateChangeWrite request, ILicenseLifecycle service, HttpContext context) =>
            {
                var result = await service.ChangeStateAsync(Actor(context), id, Key(context), Match(context), command, request.Reason, context.TraceIdentifier, context.RequestAborted);
                context.Response.Headers.ETag = result.Etag;
                return Results.Ok(result);
            }).AddEndpointFilter<AdminCsrfFilter>();
        }
        group.MapGet("/{id:guid}/activations", async (Guid id, string? cursor, int? pageSize, ILicenseLifecycle service, HttpContext context) =>
            Results.Ok(await service.ActivationsAsync(Actor(context), id, cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapGet("/{id:guid}/renewals", async (Guid id, string? cursor, int? pageSize, ILicenseLifecycle service, HttpContext context) =>
            Results.Ok(await service.RenewalsAsync(Actor(context), id, cursor, pageSize ?? 25, context.RequestAborted)));
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].Count == 1
        && Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty
        ? key : throw new ManagementException("validation_failed", 400);
    private static string Match(HttpContext context) => context.Request.Headers.IfMatch.Count == 1
        ? context.Request.Headers.IfMatch.ToString() : throw new ManagementException("precondition_required", 428);
}
