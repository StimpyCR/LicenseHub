using System.Text.RegularExpressions;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

// Privileged offline operation. Never register a public recovery endpoint for this service.
public sealed class AdminRecovery(LicenseHubDbContext db, UserManager<AdminUser> users, TimeProvider clock) : IAdminRecovery
{
    public async Task RecoverAsync(AdminRecoveryRequest request, string operatorId, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || request.Confirmation != "RECOVER:" + request.UserId
            || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 128
            || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 1024
            || request.AuthenticatorKey is null || !Regex.IsMatch(request.AuthenticatorKey, "\\A[A-Z2-7]{32}\\z")
            || request.Code is null || !Regex.IsMatch(request.Code, "\\A[0-9]{6}\\z"))
            throw new InvalidOperationException("recovery_invalid_input");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {request.UserId} FOR UPDATE", cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken);
        if (user is null || !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase)
            || !await users.IsInRoleAsync(user, SuperAdminBootstrap.RoleName))
            throw new InvalidOperationException("recovery_target_rejected");
        if (await users.GetAuthenticatorKeyAsync(user) == request.AuthenticatorKey)
            throw new InvalidOperationException("recovery_new_key_required");
        Ensure(await users.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", request.AuthenticatorKey));
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code))
            throw new InvalidOperationException("recovery_invalid_mfa");
        // Reset token exists only in memory of this trusted CLI operation.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Ensure(await users.ResetPasswordAsync(user, token, request.Password));
        Ensure(await users.SetTwoFactorEnabledAsync(user, true));
        Ensure(await users.SetLockoutEndDateAsync(user, null));
        Ensure(await users.ResetAccessFailedCountAsync(user));
        Ensure(await users.UpdateSecurityStampAsync(user));
        var now = clock.GetUtcNow();
        await db.Set<AdminSessionRow>().Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), cancellationToken);
        db.Add(new AuditLogRow
        {
            Id = Guid.NewGuid(),
            ActorId = operatorId,
            EntityId = user.Id,
            Action = "admin.recovered",
            Reason = request.Reason,
            CorrelationId = Guid.NewGuid().ToString(),
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("recovery_identity_rejected");
    }
}
