using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

public sealed class AdminReports(LicenseHubDbContext db, IApplicationAccess access, TimeProvider clock, ICursorProtection cursors) : IAdminReports
{
    public async Task<DashboardView> DashboardAsync(Guid actor, Guid applicationId, CancellationToken ct)
    {
        await Allowed(actor, applicationId, "dashboard:read", ct);
        var now = clock.GetUtcNow();
        var end = now.AddDays(7);
        var licenses = db.Set<LicenseRow>().Where(x => x.ApplicationId == applicationId);
        var active = licenses.Where(x => x.State == LicenseState.Active && (x.ExpiresAt == null || x.ExpiresAt > now));
        return new(applicationId, now.UtcDateTime, await active.CountAsync(ct),
            await licenses.CountAsync(x => x.State == LicenseState.Pending && (x.ExpiresAt == null || x.ExpiresAt > now), ct),
            await active.CountAsync(x => x.ExpiresAt != null && x.ExpiresAt <= end, ct),
            await db.Set<CustomerRow>().CountAsync(x => x.ApplicationId == applicationId, ct));
    }

    public async Task<AuditPage> AuditAsync(Guid actor, Guid applicationId, string? action, string? cursor, int size, CancellationToken ct)
    {
        await Allowed(actor, applicationId, "audit:read", ct);
        if (size is < 1 or > 100 || action is not null && (string.IsNullOrWhiteSpace(action) || action.Length > 100)) throw Error("validation_failed", 400);
        var binding = $"audit:{applicationId}:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action ?? "")));
        var offset = 0;
        if (cursor is not null)
        {
            try
            {
                var parts = cursors.Unprotect(cursor).Split('|');
                if (parts.Length != 2 || parts[0] != binding || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset is < 0 or > 1000000) throw Error("validation_failed", 400);
            }
            catch (CryptographicException) { throw Error("validation_failed", 400); }
        }
        var query = db.Set<AuditLogRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId);
        if (action is not null) query = query.Where(x => x.Action == action);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(size + 1).ToArrayAsync(ct);
        return new(rows.Take(size).Select(x => new AuditView(x.Id, applicationId, x.ActorId, x.Action, x.EntityId, x.Reason, x.CreatedAt.UtcDateTime, x.CorrelationId)).ToArray(),
            rows.Length > size ? cursors.Protect(binding + "|" + (offset + size).ToString(CultureInfo.InvariantCulture)) : null);
    }

    public async Task<byte[]> ExportAsync(Guid actor, Guid applicationId, string trace, CancellationToken ct)
    {
        await Allowed(actor, applicationId, "licenses:export", ct);
        var rows = await (from license in db.Set<LicenseRow>().AsNoTracking()
                          join customer in db.Set<CustomerRow>() on license.CustomerId equals customer.Id into customers
                          from customer in customers.DefaultIfEmpty()
                          where license.ApplicationId == applicationId
                          orderby license.CreatedAt, license.Id
                          select new { license.Id, license.State, license.CreatedAt, license.ExpiresAt, license.IsLifetime, Subject = customer == null ? "" : customer.SubjectId, Name = customer == null ? "" : customer.DisplayName }).Take(10001).ToArrayAsync(ct);
        if (rows.Length > 10000) throw Error("export_too_large", 409);
        var now = clock.GetUtcNow();
        var csv = new StringBuilder("id,state,subjectId,customerName,createdAt,expiresAt,isLifetime\r\n");
        foreach (var row in rows)
        {
            var state = row.State is LicenseState.Active or LicenseState.Pending && row.ExpiresAt <= now ? LicenseState.Expired : row.State;
            csv.AppendJoin(',', new[] { row.Id.ToString(), state.ToString(), row.Subject, row.Name ?? "", row.CreatedAt.UtcDateTime.ToString("O"), row.ExpiresAt?.UtcDateTime.ToString("O") ?? "", row.IsLifetime ? "true" : "false" }.Select(Cell)).Append("\r\n");
        }
        db.Add(new AuditLogRow { Id = Guid.NewGuid(), ApplicationId = applicationId, ActorId = actor.ToString(), Action = "licenses.exported", CorrelationId = trace, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Cell(string value)
    {
        var start = value.TrimStart();
        if (start.Length > 0 && "=+-@".Contains(start[0]) || value.Any(x => x is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private async Task Allowed(Guid actor, Guid app, string capability, CancellationToken ct)
    {
        if (!await access.IsAllowedAsync(actor, app, capability, ct) || !await db.Set<ApplicationRow>().AnyAsync(x => x.Id == app, ct)) throw Error("not_found", 404);
    }
    private static ManagementException Error(string code, int status) => new(code, status);
}
