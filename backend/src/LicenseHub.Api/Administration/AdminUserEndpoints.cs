using System.Security.Claims;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Administration;

public static class AdminUserEndpoints
{
    public static void MapAdminUsers(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/users").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        group.MapGet("/", async (string? cursor, int? pageSize, IAdminUsers service, HttpContext context) =>
            Results.Ok(await service.ListAsync(Actor(context), cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapPost("/", async (AdminUserCreate request, IAdminUsers service, HttpContext context) =>
        {
            if (context.Request.Headers["Idempotency-Key"].Count != 1 || !Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key)) throw new ManagementException("validation_failed", 400);
            var profile = await service.CreateAsync(Actor(context), key, request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = profile.Etag;
            return Results.Json(profile, statusCode: 201);
        }).AddEndpointFilter<AdminCsrfFilter>();
        group.MapPut("/{id:guid}", async (Guid id, AdminUserAccessWrite request, IAdminUsers service, HttpContext context) =>
        {
            if (context.Request.Headers.IfMatch.Count != 1) throw new ManagementException("precondition_required", 428);
            var profile = await service.UpdateAsync(Actor(context), id, context.Request.Headers.IfMatch.ToString(), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = profile.Etag;
            return Results.Ok(profile);
        }).AddEndpointFilter<AdminCsrfFilter>();
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
