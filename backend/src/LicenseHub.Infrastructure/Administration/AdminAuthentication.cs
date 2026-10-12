using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

public sealed class AdminAuthentication(LicenseHubDbContext db, UserManager<AdminUser> users, TimeProvider clock) : IAdminAuthentication
{
    // Trabajo de hash equivalente para emails desconocidos, sin credencial válida fija.
    private static readonly AdminUser DummyUser = new();
    private static readonly PasswordHasher<AdminUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public async Task<AdminChallenge> BeginAsync(AdminLoginRequest request, string correlationId, CancellationToken cancellationToken)
    {
        if (request.Email is null || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email, out var email)
            || email.Address != request.Email || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
            throw new AdminAuthenticationException("validation_failed", 400);
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            throw Denied();
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(user.Id, cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        if (user.IsDisabled || await users.IsLockedOutAsync(user))
        {
            DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            throw Denied();
        }
        if (user.PasswordHash is null) DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
        if (!await users.CheckPasswordAsync(user, request.Password) || !user.TwoFactorEnabled || !await HasAdminAccessAsync(user))
        {
            Ensure(await users.AccessFailedAsync(user));
            Audit(user.Id, "admin.login.failed", correlationId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw Denied();
        }
        var challenge = new AdminChallengeRow
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp!,
            ExpiresAt = clock.GetUtcNow().AddMinutes(5)
        };
        db.Set<AdminChallengeRow>().Add(challenge);
        Audit(user.Id, "admin.login.challenge", correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(challenge.Id, challenge.ExpiresAt);
    }

    public async Task<AdminSessionGrant> CompleteAsync(Guid challengeId, AdminMfaRequest request, string correlationId, CancellationToken cancellationToken)
    {
        if (request.Code is null || !Regex.IsMatch(request.Code, "\\A[0-9]{6}\\z"))
            throw new AdminAuthenticationException("validation_failed", 400);
        var initial = await db.Set<AdminChallengeRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
        if (initial is null) throw Denied();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(initial.UserId, cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == initial.UserId, cancellationToken);
        var challenge = await db.Set<AdminChallengeRow>().SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
        var now = clock.GetUtcNow();
        if (user is null || challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt <= now
            || challenge.SecurityStamp != user.SecurityStamp || user.IsDisabled || !user.TwoFactorEnabled
            || await users.IsLockedOutAsync(user) || !await HasAdminAccessAsync(user)) throw Denied();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Id.ToString("N") + ":" + request.Code)));
        var previous = await db.Set<AdminMfaUseRow>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.TokenHash == tokenHash, cancellationToken);
        if ((previous is not null && previous.ExpiresAt > now)
            || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code))
        {
            Ensure(await users.AccessFailedAsync(user));
            Audit(user.Id, "admin.mfa.failed", correlationId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw Denied();
        }
        if (previous is null)
            db.Set<AdminMfaUseRow>().Add(new() { UserId = user.Id, TokenHash = tokenHash, ExpiresAt = now.AddMinutes(3) });
        else previous.ExpiresAt = now.AddMinutes(3);
        challenge.ConsumedAt = now;
        Ensure(await users.ResetAccessFailedCountAsync(user));
        var session = new AdminSessionRow
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp!,
            ExpiresAt = now.AddMinutes(30)
        };
        db.Set<AdminSessionRow>().Add(session);
        Audit(user.Id, "admin.mfa.verified", correlationId);
        await db.SaveChangesAsync(cancellationToken);
        var grant = await CreateGrantAsync(session, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return grant;
    }

    public async Task<AdminSessionGrant?> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await db.Set<AdminSessionRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= clock.GetUtcNow()) return null;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled || session.SecurityStamp != user.SecurityStamp || !user.TwoFactorEnabled
            || await users.IsLockedOutAsync(user) || !await HasAdminAccessAsync(user)) return null;
        return await CreateGrantAsync(session, user, cancellationToken);
    }

    public async Task RevokeAsync(Guid sessionId, Guid userId, string correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(userId, cancellationToken);
        var session = await db.Set<AdminSessionRow>().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);
        if (session is not null && session.RevokedAt is null)
        {
            session.RevokedAt = clock.GetUtcNow();
            Audit(userId, "admin.logout", correlationId);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> HasAdminAccessAsync(AdminUser user) => await users.IsInRoleAsync(user, "SuperAdmin")
        || await db.Set<ApplicationUserGrantRow>().AnyAsync(x => x.UserId == user.Id);

    private async Task<AdminSessionGrant> CreateGrantAsync(AdminSessionRow session, AdminUser user, CancellationToken cancellationToken)
    {
        var roleNames = (await users.GetRolesAsync(user)).ToArray();
        var grants = await db.Set<ApplicationUserGrantRow>().AsNoTracking().Where(x => x.UserId == user.Id).ToArrayAsync(cancellationToken);
        var capabilities = grants.Select(x => x.Capability).Distinct().ToList();
        if (roleNames.Contains("SuperAdmin", StringComparer.Ordinal)) capabilities.Add("superadmin");
        return new(session.Id, session.ExpiresAt, new(user.Id, user.Email!, true, capabilities.Distinct().Order().ToArray(),
            grants.Select(x => x.ApplicationId).Distinct().Order().ToArray()), roleNames);
    }

    private Task<int> LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);

    private void Audit(Guid userId, string action, string correlationId) => db.Set<AuditLogRow>().Add(new()
    {
        Id = Guid.NewGuid(),
        ActorId = userId.ToString(),
        Action = action,
        EntityId = userId,
        CorrelationId = correlationId,
        CreatedAt = clock.GetUtcNow()
    });

    private static AdminAuthenticationException Denied() => new("authentication_required", 401);
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Identity write rejected.");
    }
}
