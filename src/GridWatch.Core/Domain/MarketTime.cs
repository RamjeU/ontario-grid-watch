namespace GridWatch.Core.Domain;

/// <summary>
/// The IESO runs the Ontario market on Eastern Standard Time all year: there is no daylight
/// saving shift in market data. Hours are labelled "hour ending" 1 to 24, so hour 1 covers
/// 00:00 to 01:00 EST. Everything is converted to UTC instants at the edge of the system.
/// </summary>
public static class MarketTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    /// <summary>Start of the given delivery hour, as a UTC instant.</summary>
    public static DateTime HourStartUtc(DateOnly deliveryDate, int hourEnding)
    {
        if (hourEnding is < 1 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(hourEnding), hourEnding, "Hour ending must be between 1 and 24.");
        }

        var start = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), Offset).AddHours(hourEnding - 1);
        return start.UtcDateTime;
    }

    /// <summary>Start of a 5-minute interval (1 to 12) within a delivery hour, as a UTC instant.</summary>
    public static DateTime IntervalStartUtc(DateOnly deliveryDate, int hourEnding, int interval)
    {
        if (interval is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be between 1 and 12.");
        }

        return HourStartUtc(deliveryDate, hourEnding).AddMinutes(5 * (interval - 1));
    }

    /// <summary>The market delivery date and hour ending that contain a UTC instant.</summary>
    public static (DateOnly Date, int HourEnding) FromUtc(DateTime utc)
    {
        var market = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Offset);
        return (DateOnly.FromDateTime(market.DateTime), market.Hour + 1);
    }
}
