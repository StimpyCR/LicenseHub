using System.Security.Claims;
using LicenseHub.Api.Administration;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Billing;

namespace LicenseHub.Api.Licensing;

public static class PaymentEndpoints
{
    public static void MapPayments(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/payments").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        group.MapGet("", async (Guid applicationId, string? state, string? cursor, int? pageSize, IPaymentManagement service, HttpContext context) =>
            Results.Ok(await service.ListAsync(Actor(context), applicationId, state, cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapPost("", async (PaymentWrite request, IPaymentManagement service, HttpContext context) =>
        {
            var result = await service.CreateAsync(Actor(context), Key(context), request, context.TraceIdentifier, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Json(result, statusCode: 201);
        }).AddEndpointFilter<AdminCsrfFilter>();
        foreach (var command in new[] { "confirm", "reject", "refund" })
            group.MapPost("/{id:guid}/" + command, async (Guid id, StateChangeWrite request, IPaymentManagement service, HttpContext context) =>
            {
                var result = await service.ChangeAsync(Actor(context), id, Key(context), Match(context), command, request.Reason, context.TraceIdentifier, context.RequestAborted);
                context.Response.Headers.ETag = result.Etag;
                return Results.Ok(result);
            }).AddEndpointFilter<AdminCsrfFilter>();
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty
        ? key : throw new ManagementException("validation_failed", 400);
    private static string Match(HttpContext context) => context.Request.Headers.IfMatch.Count == 1 ? context.Request.Headers.IfMatch.ToString() : throw new ManagementException("precondition_required", 428);
}
