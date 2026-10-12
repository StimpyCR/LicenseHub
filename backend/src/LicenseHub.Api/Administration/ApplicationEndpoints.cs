using System.Security.Claims;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Administration;

public static class ApplicationEndpoints
{
    public static void MapApplicationManagement(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization("AdminSession")
            .AddEndpointFilter<ManagementErrorFilter>();
        group.MapGet("/applications", async (string? cursor, int? pageSize, IApplicationManagement service, HttpContext context) =>
            Results.Ok(await service.ListAsync(Actor(context), cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapPost("/applications", async (ApplicationWrite request, IApplicationManagement service, HttpContext context) =>
        {
            var result = await service.CreateAsync(Actor(context), Key(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Json(result, statusCode: 201);
        }).AddEndpointFilter<AdminCsrfFilter>();
        group.MapPut("/applications/{id:guid}", async (Guid id, ApplicationWrite request, IApplicationManagement service, HttpContext context) =>
        {
            var result = await service.UpdateAsync(Actor(context), id, Key(context), Match(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Ok(result);
        }).AddEndpointFilter<AdminCsrfFilter>();
        group.MapGet("/applications/{id:guid}/credentials", async (Guid id, string? cursor, int? pageSize, IApplicationManagement service, HttpContext context) =>
            Results.Ok(await service.CredentialsAsync(Actor(context), id, cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapPost("/applications/{id:guid}/credentials", async (Guid id, CredentialWrite request, IApplicationManagement service, HttpContext context) =>
            Results.Json(await service.IssueAsync(Actor(context), id, Key(context), request, context.TraceIdentifier, context.RequestAborted), statusCode: 201))
            .AddEndpointFilter<AdminCsrfFilter>();
        group.MapPost("/credentials/{id:guid}/revoke", async (Guid id, StateChangeWrite request, IApplicationManagement service, HttpContext context) =>
        {
            var result = await service.RevokeAsync(Actor(context), id, Key(context), Match(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Ok(result);
        }).AddEndpointFilter<AdminCsrfFilter>();
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].Count == 1
        && Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty
        ? key : throw new ManagementException("validation_failed", 400);
    private static string Match(HttpContext context) => context.Request.Headers.IfMatch.Count == 1
        ? context.Request.Headers.IfMatch.ToString() : throw new ManagementException("precondition_required", 428);
}

public sealed class ManagementErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ManagementException exception) { return AdminAuthEndpoints.Problem(exception.Code, exception.Status); }
    }
}
