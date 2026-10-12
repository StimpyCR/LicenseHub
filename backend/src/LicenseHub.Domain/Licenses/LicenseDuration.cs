namespace LicenseHub.Domain.Licenses;

public enum DurationUnit { Minute, Hour, Day, Month, Year }

/// <summary>Duración finita. Meses/años son calendario UTC, no segundos aproximados.</summary>
public sealed record LicenseDuration
{
    public DurationUnit Unit { get; }
    public int Value { get; }

    public LicenseDuration(DurationUnit unit, int value)
    {
        if (!Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        if (value is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(value));
        Unit = unit;
        Value = value;
    }

    public DateTimeOffset AddTo(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return Unit switch
        {
            DurationUnit.Minute => utc.AddMinutes(Value),
            DurationUnit.Hour => utc.AddHours(Value),
            DurationUnit.Day => utc.AddDays(Value),
            DurationUnit.Month => utc.AddMonths(Value),
            DurationUnit.Year => utc.AddYears(Value),
            _ => throw new InvalidOperationException("Unidad de duración desconocida.")
        };
    }
}
