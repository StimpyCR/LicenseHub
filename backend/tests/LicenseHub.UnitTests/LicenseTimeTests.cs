using LicenseHub.Domain.Licenses;

namespace LicenseHub.UnitTests;

public sealed class LicenseTimeTests
{
    [Theory]
    [InlineData("2026-01-31T12:00:00Z", DurationUnit.Month, 1, "2026-02-28T12:00:00Z")]
    [InlineData("2024-01-31T12:00:00Z", DurationUnit.Month, 1, "2024-02-29T12:00:00Z")]
    [InlineData("2024-02-29T12:00:00Z", DurationUnit.Year, 1, "2025-02-28T12:00:00Z")]
    [InlineData("2026-01-31T12:00:00Z", DurationUnit.Month, 2, "2026-03-31T12:00:00Z")]
    [InlineData("2026-10-11T12:00:00Z", DurationUnit.Minute, 2, "2026-10-11T12:02:00Z")]
    public void CalendarAndShortDurationsHaveExactMeaning(string start, DurationUnit unit, int value, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), new LicenseDuration(unit, value).AddTo(DateTimeOffset.Parse(start)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000001)]
    public void RejectsInvalidDuration(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LicenseDuration(DurationUnit.Minute, value));

    [Fact]
    public void ExpirationBoundaryDeniesPremiumEvenBeforeBackgroundJob()
    {
        var expiry = DateTimeOffset.Parse("2026-10-11T12:02:00Z");
        Assert.Equal(LicenseState.Active, LicenseValidity.EffectiveState(LicenseState.Active, false, expiry, expiry.AddTicks(-1)));
        Assert.Equal(LicenseState.Expired, LicenseValidity.EffectiveState(LicenseState.Active, false, expiry, expiry));
    }

    [Theory]
    [InlineData(LicenseState.Suspended)]
    [InlineData(LicenseState.Revoked)]
    [InlineData(LicenseState.Cancelled)]
    public void BlockingStateDoesNotBecomeActiveForLifetime(LicenseState state) =>
        Assert.Equal(state, LicenseValidity.EffectiveState(state, true, null, DateTimeOffset.UtcNow));

    [Fact]
    public void CacheCannotOutliveTwoMinuteLicense()
    {
        var expiry = DateTimeOffset.Parse("2026-10-11T12:02:00Z");
        Assert.Equal(expiry, LicenseValidity.CacheUntil(LicenseState.Active, false, expiry, expiry.AddSeconds(-5)));
    }

    [Fact]
    public void DurationNormalizesTimeToUtc()
    {
        var result = new LicenseDuration(DurationUnit.Hour, 1).AddTo(DateTimeOffset.Parse("2026-10-11T06:00:00-06:00"));
        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-10-11T13:00:00Z"), result);
    }

    [Fact]
    public void RejectsInconsistentActiveFiniteLicense() =>
        Assert.Throws<ArgumentException>(() => LicenseValidity.EffectiveState(LicenseState.Active, false, null, DateTimeOffset.UtcNow));

    [Fact]
    public void LifetimeRemainsActiveAndCacheHasMaximumThirtySeconds()
    {
        var now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        Assert.Equal(LicenseState.Active, LicenseValidity.EffectiveState(LicenseState.Active, true, null, now));
        Assert.Equal(now.AddSeconds(30), LicenseValidity.CacheUntil(LicenseState.Active, true, null, now));
    }
}
