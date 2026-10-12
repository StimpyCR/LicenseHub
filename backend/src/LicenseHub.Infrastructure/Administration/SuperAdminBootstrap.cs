using System.Net.Mail;
using System.Text.RegularExpressions;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

public sealed class SuperAdminBootstrap(
    LicenseHubDbContext db,
    UserManager<AdminUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    TimeProvider clock) : ISuperAdminBootstrap
{
    public const string RoleName = "SuperAdmin";

    public async Task<Guid> InitializeAsync(SuperAdminBootstrapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Email is null || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email, out var email)
            || email.Address != request.Email || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 1024
            || request.AuthenticatorKey is null || !Regex.IsMatch(request.AuthenticatorKey, "\\A[A-Z2-7]{32}\\z")
            || request.Code is null || !Regex.IsMatch(request.Code, "\\A[0-9]{6}\\z"))
            throw new InvalidOperationException("bootstrap_invalid_input");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Una sola inicialización incluso si dos operadores arrancan al mismo tiempo.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724194631);", cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
            throw new InvalidOperationException("bootstrap_already_initialized");
        var user = new AdminUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email, LockoutEnabled = true };
        EnsureSuccess(await users.CreateAsync(user, request.Password));
        EnsureSuccess(await users.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", request.AuthenticatorKey));
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code))
            throw new InvalidOperationException("bootstrap_invalid_mfa");
        EnsureSuccess(await users.SetTwoFactorEnabledAsync(user, true));
        if (!await roles.RoleExistsAsync(RoleName))
            EnsureSuccess(await roles.CreateAsync(new IdentityRole<Guid>(RoleName) { Id = Guid.NewGuid() }));
        EnsureSuccess(await users.AddToRoleAsync(user, RoleName));
        // El email no se marca verificado: no hubo verificación de su bandeja de entrada.
        db.Set<AuditLogRow>().Add(new AuditLogRow
        {
            Id = Guid.NewGuid(),
            ActorId = user.Id.ToString(),
            Action = "admin.bootstrap",
            EntityId = user.Id,
            CorrelationId = Guid.NewGuid().ToString(),
            CreatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("bootstrap_identity_rejected:" + string.Join(',', result.Errors.Select(x => x.Code)));
    }
}
