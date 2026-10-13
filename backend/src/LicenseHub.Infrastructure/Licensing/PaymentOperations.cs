using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LicenseHub.Application.Billing;
using LicenseHub.Application.Licensing;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Licensing;

public sealed partial class LicenseOperations : IPaymentManagement
{
    public async Task<PaymentPage> ListAsync(Guid actor, Guid applicationId, string? state, string? cursor, int pageSize, CancellationToken ct)
    {
        await AllowedAsync(actor, applicationId, "payments:read", ct);
        if (state is not null and not ("Pending" or "Confirmed" or "Rejected" or "Refunded")) throw Error("validation_failed", 400);
        var binding = $"payments:{applicationId}:{state}";
        var offset = Offset(cursor, pageSize, binding);
        var query = db.Set<PaymentRow>().AsNoTracking().Where(x => x.ApplicationId == applicationId);
        if (state is not null) query = query.Where(x => x.State == state);
        var rows = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(pageSize + 1).ToArrayAsync(ct);
        return new(rows.Take(pageSize).Select(View).ToArray(), Next(rows.Length, offset, pageSize, binding));
    }

    public async Task<PaymentView> CreateAsync(Guid actor, Guid key, PaymentWrite request, string trace, CancellationToken ct)
    {
        await AllowedAsync(actor, request.ApplicationId, "payments:manage", ct);
        if (request.Amount is null || !Regex.IsMatch(request.Amount, "\\A[0-9]{1,15}\\.[0-9]{4}\\z")
            || !decimal.TryParse(request.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0
            || request.Currency is null || !Regex.IsMatch(request.Currency, "\\A[A-Z]{3}\\z")
            || string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 120) throw Error("validation_failed", 400);
        if (request.RenewalIntent is not null)
        {
            ValidateTerm(request.RenewalIntent.Term, "OnIssue");
            Reason(request.RenewalIntent.Reason);
            if (request.RenewalIntent.OperationId == Guid.Empty) throw Error("validation_failed", 400);
        }
        return await MutateAsync(actor.ToString(), request.ApplicationId, key, "payment.create", request, async () =>
        {
            if (!await db.Set<LicenseRow>().AnyAsync(x => x.Id == request.LicenseId && x.ApplicationId == request.ApplicationId && x.CustomerId == request.CustomerId, ct))
                throw Error("not_found", 404);
            var row = new PaymentRow
            {
                Id = Guid.NewGuid(),
                ApplicationId = request.ApplicationId,
                LicenseId = request.LicenseId,
                CustomerId = request.CustomerId,
                Amount = amount,
                Currency = request.Currency,
                Reference = request.Reference,
                State = "Pending",
                RenewalIntentJson = request.RenewalIntent is null ? null : JsonSerializer.Serialize(request.RenewalIntent, JsonSerializerOptions.Web),
                CreatedAt = clock.GetUtcNow(),
                Version = 1
            };
            db.Add(row);
            Audit(actor.ToString(), row.ApplicationId, row.Id, "payment.created", trace);
            return View(row);
        }, null, ct);
    }

    public async Task<PaymentView> ChangeAsync(Guid actor, Guid id, Guid key, string etag, string command, string reason, string trace, CancellationToken ct)
    {
        Reason(reason);
        if (command is not ("confirm" or "reject" or "refund")) throw Error("validation_failed", 400);
        var metadata = await db.Set<PaymentRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("not_found", 404);
        await AllowedAsync(actor, metadata.ApplicationId, "payments:" + command, ct);
        return await MutateAsync(actor.ToString(), metadata.ApplicationId, key, "payment." + command + ":" + id, new { reason, etag }, async () =>
        {
            var row = await db.Set<PaymentRow>().FromSqlInterpolated($"SELECT * FROM \"Payments\" WHERE \"Id\" = {id} AND \"ApplicationId\" = {metadata.ApplicationId} FOR UPDATE").SingleAsync(ct);
            var target = command switch { "confirm" => "Confirmed", "reject" => "Rejected", _ => "Refunded" };
            if (row.State == target) return View(row);
            Match(row.Version, etag);
            if ((command is "confirm" or "reject" && row.State != "Pending") || (command == "refund" && row.State != "Confirmed")) throw Error("invalid_transition", 409);
            if (command == "confirm" && row.RenewalIntentJson is not null)
            {
                var intent = JsonSerializer.Deserialize<RenewalWrite>(row.RenewalIntentJson, JsonSerializerOptions.Web)!;
                if (intent.Reactivate) await AllowedAsync(actor, row.ApplicationId, "licenses:reactivate", ct);
                var licenseMetadata = await db.Set<LicenseRow>().AsNoTracking().SingleAsync(x => x.Id == row.LicenseId && x.ApplicationId == row.ApplicationId, ct);
                var license = await LockLicenseAsync(licenseMetadata, ct);
                await ApplyRenewalAsync(actor, license, intent, row.Id, trace, ct);
            }
            row.State = target;
            row.Version++;
            if (command == "confirm") row.ConfirmedAt = clock.GetUtcNow();
            Audit(actor.ToString(), row.ApplicationId, id, "payment." + command, trace, reason);
            return View(row);
        }, null, ct);
    }

    private static PaymentView View(PaymentRow row) => new(row.Id, row.ApplicationId, row.LicenseId, row.CustomerId,
        row.Amount.ToString("F4", CultureInfo.InvariantCulture), row.Currency, row.State, row.Reference, row.CreatedAt.UtcDateTime, Tag(row.Version),
        row.RenewalIntentJson is null ? null : JsonSerializer.Deserialize<RenewalWrite>(row.RenewalIntentJson, JsonSerializerOptions.Web), row.ConfirmedAt?.UtcDateTime);
}
