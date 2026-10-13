using LicenseHub.Application.Licensing;
using LicenseHub.Domain.Licenses;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Licensing;

public sealed partial class LicenseOperations
{
    public async Task<LicenseView> ChangeStateAsync(Guid actor, Guid id, Guid key, string etag, string command, string reason, string trace, CancellationToken ct)
    {
        Reason(reason);
        if (command is not ("suspend" or "revoke" or "cancel" or "reactivate")) throw Error("validation_failed", 400);
        var metadata = await MetadataAsync(actor, id, "licenses:" + command, ct);
        return await MutateAsync(actor.ToString(), metadata.ApplicationId, key, "license." + command + ":" + id, new { reason, etag }, async () =>
        {
            var row = await LockLicenseAsync(metadata, ct);
            Match(row.Version, etag);
            var now = clock.GetUtcNow();
            var effective = LicenseValidity.EffectiveState(row.State, row.IsLifetime, row.ExpiresAt, now);
            var remaining = row.IsLifetime || row.ExpiresAt is null || row.ExpiresAt > now;
            LicenseState target;
            try { target = LicenseTransitions.Change(effective, command, row.ActivatedAt is not null, remaining); }
            catch (InvalidOperationException) { throw Error("invalid_transition", 409); }
            if (target == LicenseState.Active) await EnsureSlotAsync(row, ct);
            row.State = target;
            row.Version++;
            row.EntitlementVersion++;
            await InvalidateNotificationsAsync(row, ct);
            if (command is "suspend" or "revoke") NotificationEvent(row, command == "suspend" ? "Suspended" : "Revoked");
            else if (command == "reactivate") NotificationEvent(row, "Schedule");
            Audit(actor.ToString(), row.ApplicationId, id, "license." + command, trace, reason);
            return await ViewAsync(row, ct);
        }, null, ct);
    }

    public async Task<LicenseView> RenewAsync(Guid actor, Guid id, Guid key, string etag, RenewalWrite request, string trace, CancellationToken ct)
    {
        Reason(request.Reason);
        ValidateTerm(request.Term, "OnIssue");
        if (request.OperationId == Guid.Empty) throw Error("validation_failed", 400);
        var metadata = await MetadataAsync(actor, id, "licenses:renew", ct);
        if (request.Reactivate) await AllowedAsync(actor, metadata.ApplicationId, "licenses:reactivate", ct);
        return await MutateAsync(actor.ToString(), metadata.ApplicationId, key, "license.renew:" + id, new { request, etag }, async () =>
        {
            var row = await LockLicenseAsync(metadata, ct);
            Match(row.Version, etag);
            await ApplyRenewalAsync(actor, row, request, null, trace, ct);
            return await ViewAsync(row, ct);
        }, null, ct);
    }

    // Shared transactional core for future authorized payment confirmation; never opens a nested transaction.
    private async Task ApplyRenewalAsync(Guid actor, LicenseRow row, RenewalWrite request, Guid? paymentId, string trace, CancellationToken ct)
    {
        if (await db.Set<LicenseRenewalRow>().AnyAsync(x => x.ApplicationId == row.ApplicationId && x.OperationId == request.OperationId, ct))
            throw Error("renewal_already_applied", 409);
        var now = clock.GetUtcNow();
        var state = LicenseValidity.EffectiveState(row.State, row.IsLifetime, row.ExpiresAt, now);
        if (!LicenseTransitions.CanRenew(state, row.ActivatedAt is not null, request.Reactivate)
            || (row.IsLifetime && (request.Term.Kind != "Lifetime" || state == LicenseState.Active))) throw Error("invalid_transition", 409);
        var basis = row.ExpiresAt > now ? row.ExpiresAt.Value : now;
        var expires = Expiry(request.Term, request.Term.Kind == "Until" ? now : basis);
        if (expires is not null && expires <= basis) throw Error("validation_failed", 400);
        await EnsureSlotAsync(row, ct);
        var renewal = new LicenseRenewalRow
        {
            Id = Guid.NewGuid(),
            ApplicationId = row.ApplicationId,
            LicenseId = row.Id,
            OperationId = request.OperationId,
            PaymentId = paymentId,
            BeforeExpiresAt = row.ExpiresAt,
            AfterExpiresAt = expires,
            BeforeLifetime = row.IsLifetime,
            AfterLifetime = request.Term.Kind == "Lifetime",
            Reactivated = state is LicenseState.Suspended or LicenseState.Revoked,
            ActorId = actor,
            Reason = request.Reason,
            CreatedAt = now,
            Version = 1
        };
        row.IsLifetime = renewal.AfterLifetime;
        row.ExpiresAt = expires;
        row.State = LicenseState.Active;
        row.Version++;
        row.EntitlementVersion++;
        await InvalidateNotificationsAsync(row, ct);
        db.Add(renewal);
        NotificationEvent(row, "Renewed");
        Audit(actor.ToString(), row.ApplicationId, row.Id, "license.renewed", trace, request.Reason);
    }

    public async Task<ActivationPage> ActivationsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct)
    {
        var metadata = await MetadataAsync(actor, id, "licenses:read", ct);
        var binding = $"activations:{metadata.ApplicationId}:{id}";
        var offset = Offset(cursor, pageSize, binding);
        var rows = await db.Set<LicenseActivationRow>().AsNoTracking().Where(x => x.ApplicationId == metadata.ApplicationId && x.LicenseId == id)
            .OrderBy(x => x.ActivatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        return new(rows.Take(pageSize).Select(x => new ActivationRecord(x.Id, x.ApplicationId, x.LicenseId, x.CustomerId, x.SubjectId, x.ActivatedAt.UtcDateTime)).ToArray(),
            Next(rows.Length, offset, pageSize, binding));
    }

    public async Task<RenewalPage> RenewalsAsync(Guid actor, Guid id, string? cursor, int pageSize, CancellationToken ct)
    {
        var metadata = await MetadataAsync(actor, id, "licenses:read", ct);
        var binding = $"renewals:{metadata.ApplicationId}:{id}";
        var offset = Offset(cursor, pageSize, binding);
        var rows = await db.Set<LicenseRenewalRow>().AsNoTracking().Where(x => x.ApplicationId == metadata.ApplicationId && x.LicenseId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        return new(rows.Take(pageSize).Select(x => new RenewalRecord(x.Id, x.ApplicationId, x.LicenseId, x.OperationId, x.PaymentId,
            x.BeforeExpiresAt?.UtcDateTime, x.AfterExpiresAt?.UtcDateTime, x.BeforeLifetime, x.AfterLifetime, x.Reactivated, x.ActorId, x.Reason, x.CreatedAt.UtcDateTime)).ToArray(),
            Next(rows.Length, offset, pageSize, binding));
    }

    private async Task<LicenseRow> MetadataAsync(Guid actor, Guid id, string capability, CancellationToken ct)
    {
        var row = await db.Set<LicenseRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("not_found", 404);
        await AllowedAsync(actor, row.ApplicationId, capability, ct);
        return row;
    }
    private async Task<LicenseRow> LockLicenseAsync(LicenseRow metadata, CancellationToken ct)
    {
        // Same ordering as activation: subject lock, then the license row. Future renewal/payment flows share it.
        if (metadata.CustomerId is not null)
        {
            var subject = await db.Set<CustomerRow>().Where(x => x.ApplicationId == metadata.ApplicationId && x.Id == metadata.CustomerId).Select(x => x.SubjectId).SingleAsync(ct);
            await SubjectLockAsync(metadata.ApplicationId, subject, ct);
        }
        var row = await db.Set<LicenseRow>().FromSqlInterpolated($"SELECT * FROM \"Licenses\" WHERE \"Id\" = {metadata.Id} AND \"ApplicationId\" = {metadata.ApplicationId} FOR UPDATE").SingleAsync(ct);
        if (row.CustomerId != metadata.CustomerId) throw Error("precondition_failed", 412);
        return row;
    }
    private async Task EnsureSlotAsync(LicenseRow row, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var others = await db.Set<LicenseRow>().Where(x => x.ApplicationId == row.ApplicationId && x.CustomerId == row.CustomerId && x.Id != row.Id
            && x.ActivatedAt != null && (x.State == LicenseState.Active || x.State == LicenseState.Suspended)).ToArrayAsync(ct);
        foreach (var other in others)
        {
            if (LicenseValidity.EffectiveState(other.State, other.IsLifetime, other.ExpiresAt, now) != LicenseState.Expired) throw Error("subject_has_license", 409);
            other.State = LicenseState.Expired;
            other.Version++;
            other.EntitlementVersion++;
        }
        await db.SaveChangesAsync(ct);
    }
    private static void Reason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000) throw Error("validation_failed", 400);
    }
    private Task<int> InvalidateNotificationsAsync(LicenseRow row, CancellationToken ct) => db.Set<NotificationLogRow>()
        .Where(x => x.LicenseId == row.Id && x.ApplicationId == row.ApplicationId && x.EntitlementVersion != row.EntitlementVersion
            && (x.State == "Pending" || x.State == "Failed"))
        .ExecuteUpdateAsync(set => set.SetProperty(x => x.State, "Cancelled").SetProperty(x => x.LastErrorRedacted, "obsolete_entitlement"), ct);
}
