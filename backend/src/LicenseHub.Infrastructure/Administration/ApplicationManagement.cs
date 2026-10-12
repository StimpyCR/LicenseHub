using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Integration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LicenseHub.Infrastructure.Administration;

public sealed class ApplicationManagement(LicenseHubDbContext db, UserManager<AdminUser> users,
    IApplicationAccess access, TimeProvider clock) : IApplicationManagement
{
    public async Task<ApplicationPage> ListAsync(Guid actor, string? cursor, int pageSize, CancellationToken ct)
    {
        var offset = Offset(cursor, pageSize);
        var query = db.Set<ApplicationRow>().AsNoTracking();
        if (!await IsSuperAsync(actor)) query = query.Where(app => db.Set<ApplicationUserGrantRow>()
            .Any(grant => grant.UserId == actor && grant.ApplicationId == app.Id && grant.Capability == "applications:read"));
        var rows = await query.OrderBy(app => app.Slug).Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        return new(rows.Take(pageSize).Select(View).ToArray(), Next(rows.Count, offset, pageSize));
    }

    public async Task<ApplicationView> CreateAsync(Guid actor, Guid key, ApplicationWrite request, string trace, CancellationToken ct)
    {
        await SuperAsync(actor);
        Validate(request);
        return await MutateAsync(actor, key, "application.create", request, false, () =>
        {
            var row = new ApplicationRow
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Slug = request.Slug,
                IsActive = request.IsActive,
                CreatedAt = clock.GetUtcNow(),
                Version = 1
            };
            db.Add(row);
            Audit(actor, row.Id, row.Id, "application.created", trace);
            return Task.FromResult((row.Id, View(row)));
        }, ct);
    }

    public async Task<ApplicationView> UpdateAsync(Guid actor, Guid id, Guid key, string etag, ApplicationWrite request, string trace, CancellationToken ct)
    {
        await SuperAsync(actor);
        Validate(request);
        return await MutateAsync(actor, key, "application.update:" + id, new { request, etag }, false, async () =>
        {
            var row = await db.Set<ApplicationRow>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404);
            Match(row.Version, etag);
            if (request.Slug != row.Slug) throw Error(400);
            row.Name = request.Name;
            row.IsActive = request.IsActive;
            row.Version++;
            Audit(actor, row.Id, row.Id, "application.updated", trace);
            return (row.Id, View(row));
        }, ct);
    }

    public async Task<CredentialPage> CredentialsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, id, "credentials:read", ct);
        var offset = Offset(cursor, pageSize);
        var rows = await db.Set<ApplicationCredentialRow>().AsNoTracking().Where(x => x.ApplicationId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        return new(rows.Take(pageSize).Select(View).ToArray(), Next(rows.Count, offset, pageSize));
    }

    public async Task<CredentialIssued> IssueAsync(Guid actor, Guid id, Guid key, CredentialWrite request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, id, "credentials:manage", ct);
        var now = clock.GetUtcNow();
        if (request.Scopes is not { Length: > 0 and <= 3 } || request.Scopes.Any(x => !IntegrationScopes.IsValid(x))
            || request.Scopes.Distinct().Count() != request.Scopes.Length || request.ExpiresAt.Offset != TimeSpan.Zero) throw Error(400);
        return await MutateAsync(actor, key, "credential.issue:" + id, request, true, () =>
        {
            if (request.ExpiresAt <= now || request.ExpiresAt > now.AddDays(365)) throw Error(400);
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var row = new ApplicationCredentialRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = id,
                CreatedAt = now,
                Version = 1,
                ExpiresAt = request.ExpiresAt,
                Scopes = request.Scopes.Order().ToArray(),
                SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)))
            };
            db.Add(row);
            Audit(actor, id, row.Id, "credential.issued", trace);
            return Task.FromResult((id, new CredentialIssued { Credential = View(row), Secret = secret }));
        }, ct);
    }

    public async Task<CredentialView> RevokeAsync(Guid actor, Guid id, Guid key, string etag, StateChangeWrite request, string trace, CancellationToken ct)
    {
        var row = await db.Set<ApplicationCredentialRow>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error(404);
        await AllowedAsync(actor, row.ApplicationId, "credentials:manage", ct);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000) throw Error(400);
        return await MutateAsync(actor, key, "credential.revoke:" + id, new { request, etag }, false, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            Match(row.Version, etag);
            if (row.RevokedAt is null)
            {
                row.RevokedAt = clock.GetUtcNow();
                row.Version++;
                Audit(actor, row.ApplicationId, row.Id, "credential.revoked", trace, request.Reason);
            }
            return (row.ApplicationId, View(row));
        }, ct);
    }

    public async Task<FeaturePage> FeaturesAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, id, "features:read", ct);
        var offset = Offset(cursor, pageSize);
        var rows = await db.Set<ApplicationFeatureRow>().AsNoTracking().Where(x => x.ApplicationId == id)
            .OrderBy(x => x.Code).Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        return new(rows.Take(pageSize).Select(View).ToArray(), Next(rows.Count, offset, pageSize));
    }

    public async Task<FeatureView> SaveFeatureAsync(Guid actor, Guid id, string? code, Guid key, string? etag,
        FeatureWrite request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, id, "features:manage", ct);
        if (request.Code is null || !Regex.IsMatch(request.Code, "\\A[a-z][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || (code is not null && code != request.Code)) throw Error(400);
        return await MutateAsync(actor, key, "feature." + (code is null ? "create:" : "update:") + id + ":" + request.Code,
            new { request, etag }, false, async () =>
            {
                ApplicationFeatureRow row;
                if (code is null)
                {
                    row = new()
                    {
                        Id = Guid.NewGuid(),
                        ApplicationId = id,
                        CreatedAt = clock.GetUtcNow(),
                        Version = 1,
                        Code = request.Code,
                        Name = request.Name,
                        IsActive = request.IsActive
                    };
                    db.Add(row);
                }
                else
                {
                    row = await db.Set<ApplicationFeatureRow>().SingleOrDefaultAsync(x => x.ApplicationId == id && x.Code == code, ct) ?? throw Error(404);
                    Match(row.Version, etag ?? "");
                    row.Name = request.Name;
                    row.IsActive = request.IsActive;
                    row.Version++;
                }
                Audit(actor, id, row.Id, code is null ? "feature.created" : "feature.updated", trace);
                return (id, View(row));
            }, ct);
    }

    private static FeatureView View(ApplicationFeatureRow row) => new(row.ApplicationId, row.Code, row.Name, row.IsActive, Tag(row.Version));

    private async Task<T> MutateAsync<T>(Guid actor, Guid key, string operation, object request, bool secret,
        Func<Task<(Guid ApplicationId, T Value)>> change, CancellationToken ct)
    {
        if (key == Guid.Empty) throw Error(400);
        var principal = actor.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize retries before inspecting the idempotency record, including global app creation.
        var lockKey = principal + ":" + operation + ":" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.Set<IdempotencyRow>().AsNoTracking().SingleOrDefaultAsync(
            x => x.PrincipalId == principal && x.Operation == operation && x.Key == key, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash) throw new ManagementException("idempotency_conflict", 409);
            if (secret) throw new ManagementException("secret_not_recoverable", 409);
            return JsonSerializer.Deserialize<T>(prior.ResponseJson!)!;
        }
        try
        {
            var (applicationId, value) = await change();
            db.Add(new IdempotencyRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                PrincipalId = principal,
                Operation = operation,
                Key = key,
                RequestHash = hash,
                ResponseJson = secret ? null : JsonSerializer.Serialize(value),
                ResponseStatus = operation.Contains("create") || secret ? 201 : 200,
                State = "Completed",
                CreatedAt = clock.GetUtcNow(),
                RetainUntil = clock.GetUtcNow().AddDays(30)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return value;
        }
        catch (DbUpdateConcurrencyException) { throw Error(412); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ManagementException("conflict", 409); }
    }

    private async Task<bool> IsSuperAsync(Guid actor)
    {
        var user = await users.FindByIdAsync(actor.ToString());
        return user is not null && user.TwoFactorEnabled && !await users.IsLockedOutAsync(user) && await users.IsInRoleAsync(user, "SuperAdmin");
    }
    private async Task SuperAsync(Guid actor) { if (!await IsSuperAsync(actor)) throw Error(403); }
    private async Task AllowedAsync(Guid actor, Guid id, string capability, CancellationToken ct)
    {
        if (!await access.IsAllowedAsync(actor, id, capability, ct)
            || !await db.Set<ApplicationRow>().AnyAsync(x => x.Id == id, ct)) throw Error(404);
    }
    private void Audit(Guid actor, Guid app, Guid entity, string action, string trace, string? reason = null) => db.Add(
        new AuditLogRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = app,
            ActorId = actor.ToString(),
            EntityId = entity,
            Action = action,
            Reason = reason,
            CorrelationId = trace,
            CreatedAt = clock.GetUtcNow()
        });
    private static void Validate(ApplicationWrite request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || request.Slug is null
            || !Regex.IsMatch(request.Slug, "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)) throw Error(400);
    }
    private static void Match(long version, string etag)
    {
        if (string.IsNullOrEmpty(etag)) throw Error(428);
        if (etag != Tag(version)) throw Error(412);
    }
    private static int Offset(string? cursor, int size)
    {
        if (size is < 1 or > 100) throw Error(400);
        if (cursor is null) return 0;
        if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0 || offset > 1000000) throw Error(400);
        return offset;
    }
    private static string? Next(int count, int offset, int size) => count > size ? (offset + size).ToString(CultureInfo.InvariantCulture) : null;
    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";
    private static ApplicationView View(ApplicationRow row) => new(row.Id, row.Name, row.Slug, row.IsActive, row.CreatedAt.UtcDateTime, Tag(row.Version));
    private static CredentialView View(ApplicationCredentialRow row) => new(row.Id, row.ApplicationId, row.Scopes, row.CreatedAt.UtcDateTime,
        row.ExpiresAt.UtcDateTime, row.RevokedAt?.UtcDateTime, Tag(row.Version));
    private static ManagementException Error(int status) => new(status switch
    {
        400 => "validation_failed",
        403 => "forbidden",
        404 => "not_found",
        412 => "precondition_failed",
        428 => "precondition_required",
        _ => "internal_error"
    }, status);
}
