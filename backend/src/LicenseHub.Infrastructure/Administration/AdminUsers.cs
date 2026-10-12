using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LicenseHub.Infrastructure.Administration;

public sealed class AdminUsers(LicenseHubDbContext db, UserManager<AdminUser> users, TimeProvider clock) : IAdminUsers
{
    private static readonly string[] Capabilities = ["applications:read", "credentials:read", "credentials:manage", "features:read", "features:manage"];
    public async Task<AdminUserPage> ListAsync(Guid actor, string? cursor, int size, CancellationToken ct)
    {
        await SuperAsync(actor);
        var offset = 0;
        if (size is < 1 or > 100 || (cursor is not null && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0 || offset > 1000000))) throw Error(400);
        var rows = await db.Users.AsNoTracking().OrderBy(x => x.NormalizedEmail).ThenBy(x => x.Id).Skip(offset).Take(size + 1).ToArrayAsync(ct);
        var profiles = new List<AdminUserProfile>();
        foreach (var row in rows.Take(size)) profiles.Add(await ViewAsync(row, ct));
        return new(profiles.ToArray(), rows.Length > size ? (offset + size).ToString(CultureInfo.InvariantCulture) : null);
    }
    public async Task<AdminUserProfile> CreateAsync(Guid actor, Guid key, AdminUserCreate request, string trace, CancellationToken ct)
    {
        await SuperAsync(actor);
        if (key == Guid.Empty || request.Email is null || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email, out var email) || email.Address != request.Email) throw Error(400);
        var identity = actor.ToString("N") + ":admin.create:" + key.ToString("N");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", ct);
        var existing = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.Email, request.Email, StringComparison.OrdinalIgnoreCase)) throw Error(409);
            return await ViewAsync(existing, ct);
        }
        var row = new AdminUser { Id = id, Email = request.Email, UserName = request.Email, LockoutEnabled = true, AccessVersion = 1 };
        try
        {
            var result = await users.CreateAsync(row);
            if (!result.Succeeded) throw Error(409);
            Audit(actor, id, "admin.user.created", trace, null);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await ViewAsync(row, ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { throw Error(409); }
    }
    public async Task<AdminUserProfile> UpdateAsync(Guid actor, Guid id, string etag, AdminUserAccessWrite request, string trace, CancellationToken ct)
    {
        await SuperAsync(actor);
        if (request.Grants is null || request.Grants.Length > 100 || request.Grants.Any(x => x is null || x.ApplicationId == Guid.Empty || !Capabilities.Contains(x.Capability))
            || request.Grants.Distinct().Count() != request.Grants.Length || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000) throw Error(400);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404);
        if (await users.IsInRoleAsync(user, "SuperAdmin")) throw Error(403);
        if (string.IsNullOrEmpty(etag)) throw Error(428);
        if (etag != Tag(user.AccessVersion)) throw Error(412);
        var ids = request.Grants.Select(x => x.ApplicationId).Distinct().ToArray();
        if (await db.Set<ApplicationRow>().CountAsync(x => ids.Contains(x.Id), ct) != ids.Length) throw Error(400);
        db.RemoveRange(await db.Set<ApplicationUserGrantRow>().Where(x => x.UserId == id).ToArrayAsync(ct));
        // Persist removals before adding the same composite keys; transaction preserves atomicity.
        await db.SaveChangesAsync(ct);
        foreach (var grant in request.Grants) db.Add(new ApplicationUserGrantRow { UserId = id, ApplicationId = grant.ApplicationId, Capability = grant.Capability });
        user.IsDisabled = request.IsDisabled;
        user.AccessVersion++;
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) throw Error(409);
        var now = clock.GetUtcNow();
        await db.Set<AdminSessionRow>().Where(x => x.UserId == id && x.RevokedAt == null).ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), ct);
        Audit(actor, id, "admin.user.access.updated", trace, request.Reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ViewAsync(user, ct);
    }
    private async Task SuperAsync(Guid actor)
    {
        var user = await users.FindByIdAsync(actor.ToString());
        if (user is null || user.IsDisabled || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user) || !await users.IsInRoleAsync(user, "SuperAdmin")) throw Error(403);
    }
    private async Task<AdminUserProfile> ViewAsync(AdminUser user, CancellationToken ct) => new(user.Id, user.Email!, user.IsDisabled, user.TwoFactorEnabled,
        await users.IsInRoleAsync(user, "SuperAdmin"), Tag(user.AccessVersion), await db.Set<ApplicationUserGrantRow>().AsNoTracking()
            .Where(x => x.UserId == user.Id).OrderBy(x => x.ApplicationId).ThenBy(x => x.Capability).Select(x => new AdminUserGrant(x.ApplicationId, x.Capability)).ToArrayAsync(ct));
    private void Audit(Guid actor, Guid id, string action, string trace, string? reason) => db.Add(new AuditLogRow { Id = Guid.NewGuid(), ActorId = actor.ToString(), EntityId = id, Action = action, Reason = reason, CorrelationId = trace, CreatedAt = clock.GetUtcNow() });
    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";
    private static ManagementException Error(int status) => new(status switch { 400 => "validation_failed", 403 => "forbidden", 404 => "not_found", 409 => "conflict", 412 => "precondition_failed", 428 => "precondition_required", _ => "internal_error" }, status);
}
