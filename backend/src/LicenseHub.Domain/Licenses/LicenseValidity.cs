namespace LicenseHub.Domain.Licenses;

public static class LicenseValidity
{
    public static LicenseState EffectiveState(LicenseState storedState, bool lifetime,
        DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (!Enum.IsDefined(storedState)) throw new ArgumentOutOfRangeException(nameof(storedState));
        if (lifetime && expiresAt is not null) throw new ArgumentException("Una licencia vitalicia no tiene vencimiento.");
        if (storedState is LicenseState.Revoked or LicenseState.Cancelled or LicenseState.Suspended)
            return storedState;
        if (!lifetime && expiresAt is not null && expiresAt <= now)
            return LicenseState.Expired;
        if (storedState == LicenseState.Active && !lifetime && expiresAt is null)
            throw new ArgumentException("Una licencia activa finita requiere vencimiento.");
        return storedState;
    }

    public static DateTimeOffset CacheUntil(LicenseState effectiveState, bool lifetime,
        DateTimeOffset? expiresAt, DateTimeOffset evaluatedAt)
    {
        var maximum = evaluatedAt.ToUniversalTime().AddSeconds(30);
        if (effectiveState != LicenseState.Active || lifetime) return maximum;
        if (expiresAt is null || expiresAt <= evaluatedAt)
            throw new ArgumentException("El snapshot activo requiere vigencia futura.");
        return expiresAt < maximum ? expiresAt.Value.ToUniversalTime() : maximum;
    }
}
