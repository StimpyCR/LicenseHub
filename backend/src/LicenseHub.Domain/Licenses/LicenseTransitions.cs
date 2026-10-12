namespace LicenseHub.Domain.Licenses;

public static class LicenseTransitions
{
    public static LicenseState Change(LicenseState state, string command, bool activated, bool hasRemainingTime) => (state, command) switch
    {
        (LicenseState.Pending or LicenseState.Active, "suspend") => LicenseState.Suspended,
        (LicenseState.Pending or LicenseState.Active or LicenseState.Suspended or LicenseState.Expired, "revoke") => LicenseState.Revoked,
        (not LicenseState.Cancelled, "cancel") when Enum.IsDefined(state) => LicenseState.Cancelled,
        (LicenseState.Suspended, "reactivate") when hasRemainingTime => activated ? LicenseState.Active : LicenseState.Pending,
        _ => throw new InvalidOperationException("invalid_transition")
    };

    public static bool CanRenew(LicenseState state, bool activated, bool reactivate) => activated
        && (state is LicenseState.Active or LicenseState.Expired || (reactivate && state is LicenseState.Suspended or LicenseState.Revoked));
}
