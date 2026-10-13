using System.Security.Claims;
using LicenseHub.Api.Administration;
using LicenseHub.Api.Integration;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Integration;
using LicenseHub.Application.Notifications;

namespace LicenseHub.Api.Licensing;

public static class NotificationEndpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        group.MapGet("/applications/{id:guid}/notification-settings", async (Guid id, INotificationManagement service, HttpContext context) =>
        {
            var result = await service.SettingsAsync(Actor(context), id, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag; return Results.Ok(result.Value);
        });
        group.MapPut("/applications/{id:guid}/notification-settings", async (Guid id, NotificationSettings request, INotificationManagement service, HttpContext context) =>
        {
            var result = await service.SaveSettingsAsync(Actor(context), id, Key(context), Match(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag; return Results.Ok(result.Value);
        }).AddEndpointFilter<AdminCsrfFilter>();
        group.MapGet("/applications/{id:guid}/notification-templates", async (Guid id, string? cursor, int? pageSize, INotificationManagement service, HttpContext context) =>
            Results.Ok(await service.TemplatesAsync(Actor(context), id, cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapPost("/applications/{id:guid}/notification-templates", async (Guid id, TemplateWrite request, INotificationManagement service, HttpContext context) =>
            Results.Ok(await service.SaveTemplateAsync(Actor(context), id, Key(context), request, context.TraceIdentifier, context.RequestAborted)))
            .AddEndpointFilter<AdminCsrfFilter>();
        group.MapGet("/notifications", async (Guid applicationId, string? cursor, int? pageSize, INotificationManagement service, HttpContext context) =>
            Results.Ok(await service.LogsAsync(Actor(context), applicationId, cursor, pageSize ?? 25, context.RequestAborted)));
        app.MapGet("/api/v1/licenses/warnings", async (string subjectId, string? cursor, int? pageSize, IWarningQueries service, HttpContext context) =>
            Results.Ok(await service.ListAsync(Guid.Parse(context.User.FindFirstValue(IntegrationAuthenticationHandler.ApplicationClaim)!), subjectId, cursor, pageSize ?? 25, context.RequestAborted)))
            .RequireAuthorization(IntegrationScopes.Warnings).AddEndpointFilter<ManagementErrorFilter>();
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty
        ? key : throw new ManagementException("validation_failed", 400);
    private static string Match(HttpContext context) => context.Request.Headers.IfMatch.Count == 1 ? context.Request.Headers.IfMatch.ToString() : throw new ManagementException("precondition_required", 428);
}
