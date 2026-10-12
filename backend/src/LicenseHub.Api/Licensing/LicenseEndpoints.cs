using System.Security.Claims;
using LicenseHub.Api.Administration;
using LicenseHub.Api.Integration;
using LicenseHub.Application.Integration;
using LicenseHub.Application.Licensing;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Licensing;

public static class LicenseEndpoints
{
    public static void MapLicenseOperations(this WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        admin.MapGet("/customers", async (Guid applicationId, string? cursor, int? pageSize, ILicenseManagement service, HttpContext context) =>
            Results.Ok(await service.CustomersAsync(Actor(context), applicationId, cursor, pageSize ?? 25, context.RequestAborted)));
        admin.MapPost("/customers", async (CustomerWrite request, ILicenseManagement service, HttpContext context) =>
        {
            var result = await service.CreateCustomerAsync(Actor(context), Key(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Json(result, statusCode: 201);
        }).AddEndpointFilter<AdminCsrfFilter>();
        admin.MapPut("/customers/{id:guid}", async (Guid id, CustomerContactWrite request, ILicenseManagement service, HttpContext context) =>
        {
            var result = await service.UpdateCustomerAsync(Actor(context), id, Key(context), Match(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Ok(result);
        }).AddEndpointFilter<AdminCsrfFilter>();
        admin.MapGet("/licenses", async (Guid applicationId, string? state, Guid? customerId, string? cursor, int? pageSize, ILicenseManagement service, HttpContext context) =>
            Results.Ok(await service.LicensesAsync(Actor(context), applicationId, state, customerId, cursor, pageSize ?? 25, context.RequestAborted)));
        admin.MapGet("/licenses/{id:guid}", async (Guid id, ILicenseManagement service, HttpContext context) =>
        {
            var result = await service.LicenseAsync(Actor(context), id, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Ok(result);
        });
        admin.MapPost("/licenses", async (IssueRequest request, ILicenseManagement service, HttpContext context) =>
        {
            var result = await service.IssueAsync(Actor(context), Key(context), request, context.TraceIdentifier, context.RequestAborted);
            return Results.Json(result, statusCode: result.CodesRecoverable ? 201 : 200);
        }).AddEndpointFilter<AdminCsrfFilter>();

        var integration = app.MapGroup("/api/v1/licenses").AddEndpointFilter<ManagementErrorFilter>();
        integration.MapPost("/activate", async (ActivationRequest request, ILicenseIntegration service, HttpContext context) =>
            Results.Ok(await service.ActivateAsync(Application(context), Key(context), request, context.TraceIdentifier, context.RequestAborted)))
            .RequireAuthorization(IntegrationScopes.Activate);
        integration.MapGet("/status", async (string subjectId, ILicenseIntegration service, HttpContext context) =>
            Results.Ok(await service.SnapshotAsync(Application(context), subjectId, context.RequestAborted))).RequireAuthorization(IntegrationScopes.Read);
        integration.MapGet("/features", async (string subjectId, ILicenseIntegration service, HttpContext context) =>
            Results.Ok(await service.SnapshotAsync(Application(context), subjectId, context.RequestAborted))).RequireAuthorization(IntegrationScopes.Read);
    }

    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Application(HttpContext context) => Guid.Parse(context.User.FindFirstValue(IntegrationAuthenticationHandler.ApplicationClaim)!);
    private static Guid Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].Count == 1
        && Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty
        ? key : throw new ManagementException("validation_failed", 400);
    private static string Match(HttpContext context) => context.Request.Headers.IfMatch.Count == 1
        ? context.Request.Headers.IfMatch.ToString() : throw new ManagementException("precondition_required", 428);
}
