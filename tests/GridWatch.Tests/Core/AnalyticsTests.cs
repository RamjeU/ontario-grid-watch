using GridWatch.Core.Analytics;
using GridWatch.Core.Domain;

namespace GridWatch.Tests.Core;

public class FuelMixCalculatorTests
{
    private static readonly DateTime Hour = new(2026, 10, 30, 5, 0, 0, DateTimeKind.Utc);

    private static FuelOutputReading R(Fuel fuel, int? mw, int missing = 0) => new(Hour, fuel, mw, missing);

    [Fact]
    public void Shares_sum_to_one_and_clean_share_counts_non_emitting_fuels()
    {
        var mix = FuelMixCalculator.Calculate([R(Fuel.Nuclear, 6000), R(Fuel.Hydro, 2000), R(Fuel.Gas, 2000)]);

        Assert.Equal(10000, mix.TotalMw);
        Assert.Equal(1.0, mix.Fuels.Sum(f => f.Share), precision: 10);
        Assert.Equal(0.8, mix.NonEmittingShare, precision: 10);
        Assert.True(mix.Complete);
    }

    [Fact]
    public void Carbon_intensity_is_the_output_weighted_average_of_emission_factors()
    {
        // (12 * 5000 + 490 * 5000) / 10000 = 251 g/kWh
        var mix = FuelMixCalculator.Calculate([R(Fuel.Nuclear, 5000), R(Fuel.Gas, 5000)]);

        Assert.Equal(251, mix.CarbonIntensity!.Value, precision: 10);
    }

    [Fact]
    public void Other_counts_toward_total_but_not_toward_intensity()
    {
        var mix = FuelMixCalculator.Calculate([R(Fuel.Wind, 1000), R(Fuel.Other, 1000)]);

        Assert.Equal(2000, mix.TotalMw);
        Assert.Equal(11, mix.CarbonIntensity!.Value, precision: 10);
        Assert.Equal(0.5, mix.NonEmittingShare, precision: 10);
    }

    [Fact]
    public void Missing_values_mark_the_mix_incomplete()
    {
        Assert.False(FuelMixCalculator.Calculate([R(Fuel.Nuclear, 5000), R(Fuel.Wind, null)]).Complete);
        Assert.False(FuelMixCalculator.Calculate([R(Fuel.Nuclear, 5000, missing: 1)]).Complete);
    }

    [Fact]
    public void Empty_input_gives_an_empty_mix_without_dividing_by_zero()
    {
        var mix = FuelMixCalculator.Calculate([]);

        Assert.Equal(0, mix.TotalMw);
        Assert.Null(mix.CarbonIntensity);
        Assert.False(mix.Complete);
    }
}

public class GridAggregatorTests
{
    private static readonly DateOnly Day = new(2026, 10, 30);

    [Fact]
    public void Hourly_prices_report_average_min_and_max_of_intervals()
    {
        var start = MarketTime.HourStartUtc(Day, 12);
        var prices = new[] { 10m, 20m, 60m }.Select((p, i) => new PriceReading(start.AddMinutes(5 * i), p));

        var hour = Assert.Single(GridAggregator.HourlyPrices(prices));

        Assert.Equal(30m, hour.AveragePrice);
        Assert.Equal(10m, hour.MinPrice);
        Assert.Equal(60m, hour.MaxPrice);
        Assert.Equal(3, hour.Intervals);
    }

    [Fact]
    public void Daily_generation_averages_each_fuel_across_the_market_day()
    {
        var readings = Enumerable.Range(1, 24).SelectMany(h => new[]
        {
            new FuelOutputReading(MarketTime.HourStartUtc(Day, h), Fuel.Nuclear, 7000, 0),
            new FuelOutputReading(MarketTime.HourStartUtc(Day, h), Fuel.Solar, h is >= 8 and <= 17 ? 1200 : 0, 0),
        });

        var day = Assert.Single(GridAggregator.Generation(readings, Resolution.Day));

        Assert.Equal(MarketTime.HourStartUtc(Day, 1), day.StartUtc);
        Assert.Equal(7000, day.OutputMw[Fuel.Nuclear]);
        Assert.Equal(500, day.OutputMw[Fuel.Solar]); // 10 hours x 1200 / 24
        Assert.True(day.Complete);
    }

    [Fact]
    public void Daily_summary_finds_the_peak_hour_and_flags_partial_days()
    {
        var demand = Enumerable.Range(1, 24)
            .Select(h => new DemandReading(MarketTime.HourStartUtc(Day, h), h == 18 ? 19000 : 15000, 16000))
            .Append(new DemandReading(MarketTime.HourStartUtc(Day.AddDays(1), 1), 14000, 15000));

        var summaries = GridAggregator.DailySummaries(demand, [], []);

        Assert.Equal(2, summaries.Count);
        var full = summaries.Single(s => s.MarketDate == Day);
        Assert.Equal(19000, full.PeakOntarioDemandMw);
        Assert.Equal(MarketTime.HourStartUtc(Day, 18), full.PeakHourStartUtc);
        Assert.All(summaries, s => Assert.False(s.Complete)); // no prices or fuel data
        Assert.Equal(Day.AddDays(1), summaries[0].MarketDate); // newest first
    }

    [Fact]
    public void Realtime_join_keeps_intervals_that_have_only_one_value()
    {
        var t = MarketTime.HourStartUtc(Day, 1);

        var points = GridAggregator.Realtime(
            [new LiveDemandReading(t, 15000), new LiveDemandReading(t.AddMinutes(5), 15100)],
            [new PriceReading(t, 50m)]);

        Assert.Equal(2, points.Count);
        Assert.Equal(50m, points[0].PricePerMwh);
        Assert.Null(points[1].PricePerMwh);
        Assert.Equal(15100, points[1].OntarioDemandMw);
    }
}

public class TimeRangeTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_to_the_span_ending_now()
    {
        Assert.True(TimeRange.TryCreate(null, null, TimeSpan.FromDays(7), Now, out var range, out _));
        Assert.Equal(Now.AddDays(-7), range.FromUtc);
        Assert.Equal(Now, range.ToUtc);
    }

    [Fact]
    public void Converts_offsets_to_UTC()
    {
        var from = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.True(TimeRange.TryCreate(from, null, TimeSpan.FromDays(1), Now, out var range, out _));
        Assert.Equal(new DateTime(2026, 10, 30, 4, 0, 0, DateTimeKind.Utc), range.FromUtc);
    }

    [Fact]
    public void Rejects_reversed_and_oversized_ranges()
    {
        Assert.False(TimeRange.TryCreate(Now, Now.AddDays(-1), TimeSpan.FromDays(1), Now, out _, out var reversed));
        Assert.Contains("earlier", reversed);

        Assert.False(TimeRange.TryCreate(Now.AddDays(-8), Now, TimeSpan.FromDays(1), Now, out _, out var tooLong, TimeSpan.FromDays(7)));
        Assert.Contains("7 days", tooLong);
    }
}
