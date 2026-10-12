using LicenseHub.Domain.Licenses;

namespace LicenseHub.UnitTests;

public sealed class LicenseTransitionsTests
{
    [Fact]
    public void CancellationIsTerminalAndReactivationNeedsRemainingTime()
    {
        foreach (var command in new[] { "suspend", "revoke", "cancel", "reactivate" })
            Assert.Throws<InvalidOperationException>(() => LicenseTransitions.Change(LicenseState.Cancelled, command, true, true));
        Assert.Equal(LicenseState.Pending, LicenseTransitions.Change(LicenseState.Suspended, "reactivate", false, true));
        Assert.Equal(LicenseState.Active, LicenseTransitions.Change(LicenseState.Suspended, "reactivate", true, true));
        Assert.Throws<InvalidOperationException>(() => LicenseTransitions.Change(LicenseState.Suspended, "reactivate", true, false));
        Assert.False(LicenseTransitions.CanRenew(LicenseState.Expired, false, true));
        Assert.False(LicenseTransitions.CanRenew(LicenseState.Revoked, true, false));
        Assert.True(LicenseTransitions.CanRenew(LicenseState.Revoked, true, true));
    }
}
