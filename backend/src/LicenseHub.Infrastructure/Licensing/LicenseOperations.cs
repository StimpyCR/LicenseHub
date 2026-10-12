using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LicenseHub.Application.Administration;
using LicenseHub.Application.Licensing;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LicenseHub.Infrastructure.Licensing;

public sealed class LicenseOperations(LicenseHubDbContext db, IApplicationAccess access, TimeProvider clock,
    ICursorProtection cursorProtector) : ILicenseManagement, ILicenseIntegration
{

    public async Task<CustomerPage> CustomersAsync(Guid actor, Guid applicationId, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "customers:read", ct);
        var binding = "customers:" + applicationId;
        var offset = Offset(cursor, pageSize, binding);
        var rows = await db.Set<CustomerRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        return new(rows.Take(pageSize).Select(View).ToArray(), Next(rows.Count, offset, pageSize, binding));
    }

    public async Task<CustomerView> CreateCustomerAsync(Guid actor, Guid key, CustomerWrite request, string trace, CancellationToken ct)
    {
        Subject(request.SubjectId);
        Contact(request.DisplayName, request.Email);
        await AllowedAsync(actor, request.ApplicationId, "customers:manage", ct);
        return await MutateAsync(actor.ToString(), request.ApplicationId, key, "customer.create", request, async () =>
        {
            await SubjectLockAsync(request.ApplicationId, request.SubjectId, ct);
            if (await db.Set<CustomerRow>().AnyAsync(x => x.ApplicationId == request.ApplicationId && x.SubjectId == request.SubjectId, ct))
                throw Error("conflict", 409);
            var row = new CustomerRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = request.ApplicationId,
                SubjectId = request.SubjectId,
                DisplayName = request.DisplayName,
                Email = request.Email,
                CreatedAt = clock.GetUtcNow(),
                Version = 1
            };
            db.Add(row);
            Audit(actor.ToString(), row.ApplicationId, row.Id, "customer.created", trace);
            return View(row);
        }, null, ct);
    }

    public async Task<CustomerView> UpdateCustomerAsync(Guid actor, Guid id, Guid key, string etag, CustomerContactWrite request, string trace, CancellationToken ct)
    {
        Contact(request.DisplayName, request.Email);
        var row = await db.Set<CustomerRow>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("not_found", 404);
        await AllowedAsync(actor, row.ApplicationId, "customers:manage", ct);
        return await MutateAsync(actor.ToString(), row.ApplicationId, key, "customer.update:" + id, new { request, etag }, () =>
        {
            Match(row.Version, etag);
            row.DisplayName = request.DisplayName;
            row.Email = request.Email;
            row.Version++;
            Audit(actor.ToString(), row.ApplicationId, id, "customer.updated", trace);
            return Task.FromResult(View(row));
        }, null, ct);
    }

    public async Task<LicensePage> LicensesAsync(Guid actor, Guid applicationId, string? state, Guid? customerId, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "licenses:read", ct);
        if (state is not null && (!Enum.TryParse<LicenseState>(state, out var parsed) || parsed.ToString() != state)) throw Error("validation_failed", 400);
        var binding = $"licenses:{applicationId}:{state}:{customerId}";
        var offset = Offset(cursor, pageSize, binding);
        var now = clock.GetUtcNow();
        var query = db.Set<LicenseRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId);
        if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
        if (state == "Expired") query = query.Where(x => x.State == LicenseState.Expired
            || ((x.State == LicenseState.Pending || x.State == LicenseState.Active) && !x.IsLifetime && x.ExpiresAt <= now));
        else if (state is not null)
        {
            var stored = Enum.Parse<LicenseState>(state);
            query = query.Where(x => x.State == stored);
            if (stored is LicenseState.Active or LicenseState.Pending) query = query.Where(x => x.IsLifetime || x.ExpiresAt == null || x.ExpiresAt > now);
        }
        var rows = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        var items = new List<LicenseView>();
        foreach (var row in rows.Take(pageSize)) items.Add(await ViewAsync(row, ct));
        return new(items.ToArray(), Next(rows.Count, offset, pageSize, binding));
    }

    public async Task<LicenseView> LicenseAsync(Guid actor, Guid id, CancellationToken ct)
    {
        var row = await db.Set<LicenseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("not_found", 404);
        await AllowedAsync(actor, row.ApplicationId, "licenses:read", ct);
        return await ViewAsync(row, ct);
    }

    public async Task<IssueResponse> IssueAsync(Guid actor, Guid key, IssueRequest request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, request.ApplicationId, "licenses:issue", ct);
        if (request.Quantity is < 1 or > 100 || request.CustomerId == Guid.Empty || (request.CustomerId is not null && request.Quantity != 1)
            || request.StartMode is not ("OnIssue" or "OnActivation") || request.Features is null || request.Features.Length > 100
            || request.Features.Any(x => x is null || !Regex.IsMatch(x, "\\A[a-z][a-z0-9._-]{0,63}\\z"))
            || request.Features.Distinct(StringComparer.Ordinal).Count() != request.Features.Length) throw Error("validation_failed", 400);
        ValidateTerm(request.Term, request.StartMode);
        return await MutateAsync(actor.ToString(), request.ApplicationId, key, "license.issue", request, async () =>
        {
            await ActiveApplicationAsync(request.ApplicationId, ct);
            var now = clock.GetUtcNow();
            var expires = Expiry(request.Term, now);
            if (request.CustomerId is not null && !await db.Set<CustomerRow>().AnyAsync(x => x.ApplicationId == request.ApplicationId && x.Id == request.CustomerId, ct))
                throw Error("not_found", 404);
            var available = await db.Set<ApplicationFeatureRow>().CountAsync(x => x.ApplicationId == request.ApplicationId && x.IsActive && request.Features.Contains(x.Code), ct);
            if (available != request.Features.Length) throw Error("validation_failed", 400);
            var items = new List<IssueItem>();
            for (var i = 0; i < request.Quantity; i++)
            {
                var code = "LH1_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var row = new LicenseRow
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = request.ApplicationId,
                    CustomerId = request.CustomerId,
                    KeyHash = Hash(code),
                    State = LicenseState.Pending,
                    StartMode = request.StartMode,
                    IsLifetime = request.Term.Kind == "Lifetime",
                    DurationUnit = request.Term.Duration?.Unit,
                    DurationValue = request.Term.Duration?.Value,
                    StartsAt = request.StartMode == "OnIssue" ? now : null,
                    ExpiresAt = request.StartMode == "OnIssue" ? expires : null,
                    CreatedAt = now,
                    Version = 1,
                    EntitlementVersion = 1
                };
                db.Add(row);
                foreach (var feature in request.Features) db.Add(new LicenseFeatureRow
                {
                    Id = Guid.NewGuid(),
                    ApplicationId = row.ApplicationId,
                    LicenseId = row.Id,
                    FeatureCode = feature,
                    CreatedAt = now,
                    Version = 1
                });
                Audit(actor.ToString(), row.ApplicationId, row.Id, "license.issued", trace);
                items.Add(new() { License = View(row, request.Features), LicenseKey = code });
            }
            return new IssueResponse { CodesRecoverable = true, Items = items.ToArray() };
        }, value => new IssueResponse
        {
            CodesRecoverable = false,
            Items = value.Items.Select(x => new IssueItem { License = x.License, LicenseKey = null }).ToArray()
        }, ct);
    }

    public async Task<ActivationResponse> ActivateAsync(Guid applicationId, Guid key, ActivationRequest request, string trace, CancellationToken ct)
    {
        Subject(request.SubjectId);
        if (request.LicenseKey is null || !Regex.IsMatch(request.LicenseKey, "\\ALH1_[A-Za-z0-9_-]{43}\\z")) throw Error("validation_failed", 400);
        await ActiveApplicationAsync(applicationId, ct);
        // Stable application principal permits retry after rotating integration credentials.
        return await MutateAsync("application:" + applicationId, applicationId, key, "license.activate", request, async () =>
        {
            await SubjectLockAsync(applicationId, request.SubjectId, ct);
            var hash = Hash(request.LicenseKey);
            var row = await db.Set<LicenseRow>().FromSqlInterpolated($"SELECT * FROM \"Licenses\" WHERE \"ApplicationId\" = {applicationId} AND \"KeyHash\" = {hash} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw Error("not_found", 404);
            var now = clock.GetUtcNow();
            if (row.ActivatedAt is not null) throw Error("license_already_activated", 409);
            if (LicenseValidity.EffectiveState(row.State, row.IsLifetime, row.ExpiresAt, now) != LicenseState.Pending) throw Error("license_not_activatable", 409);
            var customer = await db.Set<CustomerRow>().SingleOrDefaultAsync(x => x.ApplicationId == applicationId && x.SubjectId == request.SubjectId, ct);
            if (row.CustomerId is not null && row.CustomerId != customer?.Id) throw Error("license_subject_mismatch", 409);
            if (customer is null)
            {
                customer = new CustomerRow { Id = Guid.NewGuid(), ApplicationId = applicationId, SubjectId = request.SubjectId, CreatedAt = now, Version = 1 };
                db.Add(customer);
            }
            var prior = await db.Set<LicenseRow>().Where(x => x.ApplicationId == applicationId && x.CustomerId == customer.Id
                && x.ActivatedAt != null && (x.State == LicenseState.Active || x.State == LicenseState.Suspended)).ToListAsync(ct);
            foreach (var existing in prior)
            {
                if (LicenseValidity.EffectiveState(existing.State, existing.IsLifetime, existing.ExpiresAt, now) != LicenseState.Expired)
                    throw Error("subject_has_license", 409);
                existing.State = LicenseState.Expired;
                existing.Version++;
                existing.EntitlementVersion++;
            }
            // Release the filtered unique index before assigning the replacement license.
            await db.SaveChangesAsync(ct);
            if (row.StartMode == "OnActivation")
            {
                row.StartsAt = now;
                row.ExpiresAt = row.IsLifetime ? null : Expiry(new("Duration", new(row.DurationUnit!, row.DurationValue!.Value)), now);
            }
            row.CustomerId = customer.Id;
            row.ActivatedAt = now;
            row.State = LicenseState.Active;
            row.Version++;
            row.EntitlementVersion++;
            var activation = new LicenseActivationRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                LicenseId = row.Id,
                CustomerId = customer.Id,
                SubjectId = request.SubjectId,
                CreatedAt = now,
                ActivatedAt = now,
                Version = 1
            };
            db.Add(activation);
            Audit("application:" + applicationId, applicationId, row.Id, "license.activated", trace);
            return new ActivationResponse(activation.Id, now.UtcDateTime, await SnapshotAsync(row, request.SubjectId, ct));
        }, null, ct);
    }

    public async Task<EntitlementSnapshot> SnapshotAsync(Guid applicationId, string subjectId, CancellationToken ct)
    {
        Subject(subjectId);
        await ActiveApplicationAsync(applicationId, ct);
        var row = await (from license in db.Set<LicenseRow>().AsNoTracking()
                         join customer in db.Set<CustomerRow>() on new { license.ApplicationId, Id = license.CustomerId }
                             equals new { customer.ApplicationId, Id = (Guid?)customer.Id }
                         where license.ApplicationId == applicationId && customer.SubjectId == subjectId && license.ActivatedAt != null
                         orderby (license.State == LicenseState.Active || license.State == LicenseState.Suspended) descending,
                             license.ActivatedAt descending, license.Id descending
                         select license).FirstOrDefaultAsync(ct);
        if (row is not null) return await SnapshotAsync(row, subjectId, ct);
        var now = clock.GetUtcNow();
        return new(applicationId, subjectId, null, "None", false, [], now.UtcDateTime, now.AddSeconds(30).UtcDateTime, "0");
    }

    private async Task<EntitlementSnapshot> SnapshotAsync(LicenseRow row, string subject, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var state = LicenseValidity.EffectiveState(row.State, row.IsLifetime, row.ExpiresAt, now);
        var features = state == LicenseState.Active ? await (from granted in db.Set<LicenseFeatureRow>()
                                                             join feature in db.Set<ApplicationFeatureRow>() on new { granted.ApplicationId, Code = granted.FeatureCode }
                                                                 equals new { feature.ApplicationId, feature.Code }
                                                             where granted.LicenseId == row.Id && granted.ApplicationId == row.ApplicationId && feature.IsActive
                                                             orderby granted.FeatureCode
                                                             select granted.FeatureCode).ToArrayAsync(ct) : [];
        // During activation, features are already persisted from issuance.
        return new(row.ApplicationId, subject, new(row.Id, row.CustomerId!.Value, row.IsLifetime, row.ExpiresAt?.UtcDateTime),
            state.ToString(), state == LicenseState.Active, features, now.UtcDateTime,
            LicenseValidity.CacheUntil(state, row.IsLifetime, row.ExpiresAt, now).UtcDateTime, row.EntitlementVersion.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<T> MutateAsync<T>(string principal, Guid app, Guid key, string operation, object request, Func<Task<T>> change,
        Func<T, T>? redact, CancellationToken ct)
    {
        if (key == Guid.Empty) throw Error("validation_failed", 400);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"{app}:{principal}:{operation}:{key}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.Set<IdempotencyRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == app && x.PrincipalId == principal
            && x.Operation == operation && x.Key == key, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash) throw Error("idempotency_conflict", 409);
            return JsonSerializer.Deserialize<T>(prior.ResponseJson!)!;
        }
        try
        {
            var result = await change();
            db.Add(new IdempotencyRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = app,
                PrincipalId = principal,
                Operation = operation,
                Key = key,
                RequestHash = hash,
                ResponseJson = JsonSerializer.Serialize(redact is null ? result : redact(result)),
                ResponseStatus = operation == "license.issue" ? 201 : 200,
                State = "Completed",
                CreatedAt = clock.GetUtcNow(),
                RetainUntil = clock.GetUtcNow().AddDays(30)
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Error("precondition_failed", 412); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { throw Error("conflict", 409); }
    }

    private async Task AllowedAsync(Guid actor, Guid app, string capability, CancellationToken ct)
    {
        if (!await access.IsAllowedAsync(actor, app, capability, ct) || !await db.Set<ApplicationRow>().AnyAsync(x => x.Id == app, ct)) throw Error("not_found", 404);
    }
    private async Task ActiveApplicationAsync(Guid app, CancellationToken ct)
    {
        if (!await db.Set<ApplicationRow>().AnyAsync(x => x.Id == app && x.IsActive, ct)) throw Error("not_found", 404);
    }
    private Task<int> SubjectLockAsync(Guid app, string subject, CancellationToken ct)
    {
        var binding = $"subject:{app}:{subject}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({binding}, 0))", ct);
    }
    private void Audit(string actor, Guid app, Guid entity, string action, string trace) => db.Add(new AuditLogRow
    {
        Id = Guid.NewGuid(),
        ActorId = actor,
        ApplicationId = app,
        EntityId = entity,
        Action = action,
        CorrelationId = trace,
        CreatedAt = clock.GetUtcNow()
    });
    private async Task<LicenseView> ViewAsync(LicenseRow row, CancellationToken ct) => View(row,
        await db.Set<LicenseFeatureRow>().Where(x => x.ApplicationId == row.ApplicationId && x.LicenseId == row.Id).OrderBy(x => x.FeatureCode).Select(x => x.FeatureCode).ToArrayAsync(ct));
    private LicenseView View(LicenseRow row, string[] features) => new(row.Id, row.ApplicationId, row.CustomerId,
        LicenseValidity.EffectiveState(row.State, row.IsLifetime, row.ExpiresAt, clock.GetUtcNow()).ToString(), row.StartMode, row.IsLifetime,
        row.StartsAt?.UtcDateTime, row.ActivatedAt?.UtcDateTime, row.ExpiresAt?.UtcDateTime, features, row.CreatedAt.UtcDateTime, Tag(row.Version));
    private static CustomerView View(CustomerRow row) => new(row.Id, row.ApplicationId, row.SubjectId, row.DisplayName, row.Email, row.CreatedAt.UtcDateTime, Tag(row.Version));
    private static void Subject(string value)
    {
        if (value is null || !Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\\z")) throw Error("validation_failed", 400);
    }
    private static void Contact(string? name, string? email)
    {
        if (name?.Length > 200 || email?.Length > 254 || (email is not null && (!MailAddress.TryCreate(email, out var address) || address.Address != email))) throw Error("validation_failed", 400);
    }
    private static void ValidateTerm(TermWrite term, string mode)
    {
        if (term is null || term.Kind is not ("Duration" or "Until" or "Lifetime")
            || (term.Kind == "Lifetime" && (term.Duration is not null || term.Until is not null))
            || (term.Kind == "Until" && (mode != "OnIssue" || term.Duration is not null || term.Until is null || term.Until.Value.Offset != TimeSpan.Zero))
            || (term.Kind == "Duration" && (term.Duration is null || term.Until is not null))) throw Error("validation_failed", 400);
        if (term.Duration is not null && (!Enum.TryParse<DurationUnit>(term.Duration.Unit, out var unit) || unit.ToString() != term.Duration.Unit
            || term.Duration.Value is < 1 or > 1000000)) throw Error("validation_failed", 400);
    }
    private static DateTimeOffset? Expiry(TermWrite term, DateTimeOffset now)
    {
        if (term.Kind == "Lifetime") return null;
        try
        {
            var expires = term.Kind == "Until" ? term.Until!.Value : new LicenseDuration(Enum.Parse<DurationUnit>(term.Duration!.Unit), term.Duration.Value).AddTo(now);
            if (expires <= now) throw Error("validation_failed", 400);
            return expires;
        }
        catch (ArgumentOutOfRangeException) { throw Error("validation_failed", 400); }
    }
    private int Offset(string? cursor, int size, string binding)
    {
        if (size is < 1 or > 100) throw Error("validation_failed", 400);
        if (cursor is null) return 0;
        try
        {
            var value = cursorProtector.Unprotect(cursor).Split('|');
            if (value.Length != 2 || value[0] != binding || !int.TryParse(value[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset is < 0 or > 1000000) throw Error("validation_failed", 400);
            return offset;
        }
        catch (CryptographicException) { throw Error("validation_failed", 400); }
    }
    private string? Next(int count, int offset, int size, string binding) => count > size ? cursorProtector.Protect(binding + "|" + (offset + size).ToString(CultureInfo.InvariantCulture)) : null;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";
    private static void Match(long version, string etag)
    {
        if (string.IsNullOrEmpty(etag)) throw Error("precondition_required", 428);
        if (etag != Tag(version)) throw Error("precondition_failed", 412);
    }
    private static ManagementException Error(string code, int status) => new(code, status);
}
