using LicenseHub.Application.Administration;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Persistence.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Administration;

public sealed class ApplicationAccess(LicenseHubDbContext db, UserManager<AdminUser> users) : IApplicationAccess
{
    public async Task<bool> IsAllowedAsync(Guid userId, Guid applicationId, string capability, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || applicationId == Guid.Empty || string.IsNullOrWhiteSpace(capability)) return false;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDisabled || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user)) return false;
        if (await users.IsInRoleAsync(user, "SuperAdmin")) return true;
        return await db.Set<ApplicationUserGrantRow>().AsNoTracking().AnyAsync(
            grant => grant.UserId == userId && grant.ApplicationId == applicationId && grant.Capability == capability,
            cancellationToken);
    }
}
