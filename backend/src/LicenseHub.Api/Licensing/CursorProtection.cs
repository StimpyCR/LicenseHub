using LicenseHub.Application.Licensing;
using Microsoft.AspNetCore.DataProtection;

namespace LicenseHub.Api.Licensing;

public sealed class CursorProtection(IDataProtectionProvider provider) : ICursorProtection
{
    private readonly IDataProtector protector = provider.CreateProtector("LicenseHub.Pagination.v1");
    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
}
