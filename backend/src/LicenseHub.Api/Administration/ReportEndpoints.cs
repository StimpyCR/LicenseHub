using System.Security.Claims;
using LicenseHub.Application.Administration;

namespace LicenseHub.Api.Administration;

public static class ReportEndpoints
{
    public static void MapReports(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").RequireAuthorization("AdminSession").AddEndpointFilter<ManagementErrorFilter>();
        group.MapGet("/dashboard", async (Guid applicationId, IAdminReports reports, HttpContext context) => Results.Ok(await reports.DashboardAsync(Actor(context), applicationId, context.RequestAborted)));
        group.MapGet("/audit-logs", async (Guid applicationId, string? action, string? cursor, int? pageSize, IAdminReports reports, HttpContext context) => Results.Ok(await reports.AuditAsync(Actor(context), applicationId, action, cursor, pageSize ?? 25, context.RequestAborted)));
        group.MapGet("/licenses/export", async (Guid applicationId, IAdminReports reports, HttpContext context) => Results.File(await reports.ExportAsync(Actor(context), applicationId, context.TraceIdentifier, context.RequestAborted), "text/csv; charset=utf-8", "licenses.csv"));
    }
    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
