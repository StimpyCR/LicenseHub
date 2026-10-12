using Microsoft.AspNetCore.Identity;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class AdminUser : IdentityUser<Guid>
{
    public bool IsDisabled { get; set; }
    public long AccessVersion { get; set; }
}
