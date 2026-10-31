using GridWatch.Core.Domain;

namespace GridWatch.Tests.Core;

public class MarketTimeTests
{
    [Fact]
    public void Hour_ending_1_starts_at_midnight_EST()
    {
        var start = MarketTime.HourStartUtc(new DateOnly(2026, 1, 15), 1);

        Assert.Equal(new DateTime(2026, 1, 15, 5, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void Hour_ending_24_starts_at_23_00_EST()
    {
        Assert.Equal(new DateTime(2026, 1, 16, 4, 0, 0, DateTimeKind.Utc), MarketTime.HourStartUtc(new DateOnly(2026, 1, 15), 24));
    }

    [Fact]
    public void Market_time_ignores_daylight_saving()
    {
        // In July, Toronto clocks are on EDT (UTC-4), but the IESO market stays on EST (UTC-5).
        Assert.Equal(new DateTime(2026, 7, 1, 5, 0, 0, DateTimeKind.Utc), MarketTime.HourStartUtc(new DateOnly(2026, 7, 1), 1));
    }

    [Fact]
    public void Five_minute_intervals_step_through_the_hour()
    {
        var date = new DateOnly(2026, 10, 31);

        Assert.Equal(MarketTime.HourStartUtc(date, 12), MarketTime.IntervalStartUtc(date, 12, 1));
        Assert.Equal(MarketTime.HourStartUtc(date, 12).AddMinutes(55), MarketTime.IntervalStartUtc(date, 12, 12));
    }

    [Theory]
    [InlineData(2026, 3, 8, 1)]
    [InlineData(2026, 3, 8, 24)]
    [InlineData(2026, 12, 31, 24)]
    [InlineData(2027, 1, 1, 1)]
    public void FromUtc_round_trips(int year, int month, int day, int hourEnding)
    {
        var date = new DateOnly(year, month, day);

        Assert.Equal((date, hourEnding), MarketTime.FromUtc(MarketTime.HourStartUtc(date, hourEnding)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Rejects_hours_outside_1_to_24(int hourEnding)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketTime.HourStartUtc(new DateOnly(2026, 1, 1), hourEnding));
    }
}
