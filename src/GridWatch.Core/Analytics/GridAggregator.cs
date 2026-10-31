using GridWatch.Core.Domain;

namespace GridWatch.Core.Analytics;

/// <summary>
/// Pure functions that turn raw readings into the series the dashboard shows.
/// They run in memory on at most a few months of data, which keeps the SQL simple and
/// identical across SQLite and SQL Server.
/// </summary>
public static class GridAggregator
{
    public static IReadOnlyList<HourlyPricePoint> HourlyPrices(IEnumerable<PriceReading> intervals) =>
        intervals
            .GroupBy(p => TruncateToHour(p.IntervalStartUtc))
            .OrderBy(g => g.Key)
            .Select(g => new HourlyPricePoint(
                g.Key,
                Math.Round(g.Average(p => p.PricePerMwh), 2),
                g.Min(p => p.PricePerMwh),
                g.Max(p => p.PricePerMwh),
                g.Count()))
            .ToList();

    /// <summary>Joins 5-minute demand and price on interval start; an interval with only one of them still appears.</summary>
    public static IReadOnlyList<RealtimePoint> Realtime(IEnumerable<LiveDemandReading> demand, IEnumerable<PriceReading> prices)
    {
        var demandByStart = demand.ToDictionary(d => d.IntervalStartUtc, d => d.OntarioDemandMw);
        var priceByStart = prices.ToDictionary(p => p.IntervalStartUtc, p => p.PricePerMwh);

        return demandByStart.Keys.Union(priceByStart.Keys)
            .Order()
            .Select(t => new RealtimePoint(
                t,
                demandByStart.TryGetValue(t, out var mw) ? mw : null,
                priceByStart.TryGetValue(t, out var price) ? price : null))
            .ToList();
    }

    public static IReadOnlyList<GenerationPoint> Generation(IEnumerable<FuelOutputReading> readings, Resolution resolution)
    {
        var hourly = readings
            .GroupBy(r => r.HourStartUtc)
            .OrderBy(g => g.Key)
            .Select(g => new GenerationPoint(
                g.Key,
                g.ToDictionary(r => r.Fuel, r => r.OutputMw),
                g.All(r => r.OutputMw is not null && r.MissingPoints == 0)))
            .ToList();

        if (resolution == Resolution.Hour)
        {
            return hourly;
        }

        // Daily resolution: average MW per fuel across the market day's hours.
        return hourly
            .GroupBy(p => MarketDayStartUtc(p.StartUtc))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var fuels = g.SelectMany(p => p.OutputMw.Keys).Distinct();
                var averages = fuels.ToDictionary(
                    f => f,
                    f =>
                    {
                        var values = g.Select(p => p.OutputMw.GetValueOrDefault(f)).Where(v => v is not null).Select(v => v!.Value).ToList();
                        return values.Count > 0 ? (int?)Math.Round(values.Average()) : null;
                    });
                return new GenerationPoint(g.Key, averages, g.All(p => p.Complete) && g.Count() == 24);
            })
            .ToList();
    }

    public static IReadOnlyList<DailySummary> DailySummaries(
        IEnumerable<DemandReading> demand,
        IEnumerable<PriceReading> prices,
        IEnumerable<FuelOutputReading> generation)
    {
        var demandByDay = demand.GroupBy(d => MarketTime.FromUtc(d.HourStartUtc).Date).ToDictionary(g => g.Key, g => g.ToList());
        var pricesByDay = prices.GroupBy(p => MarketTime.FromUtc(p.IntervalStartUtc).Date).ToDictionary(g => g.Key, g => g.ToList());
        var genByDay = generation.GroupBy(r => MarketTime.FromUtc(r.HourStartUtc).Date).ToDictionary(g => g.Key, g => g.ToList());

        var days = demandByDay.Keys.Concat(pricesByDay.Keys).Concat(genByDay.Keys).Distinct().OrderByDescending(d => d);
        var result = new List<DailySummary>();

        foreach (var day in days)
        {
            DemandReading? peak = null;
            double? avgDemand = null;
            if (demandByDay.TryGetValue(day, out var d) && d.Count > 0)
            {
                peak = d.MaxBy(x => x.OntarioDemandMw);
                avgDemand = Math.Round(d.Average(x => x.OntarioDemandMw), 0);
            }

            decimal? avgPrice = pricesByDay.TryGetValue(day, out var p) && p.Count > 0
                ? Math.Round(p.Average(x => x.PricePerMwh), 2)
                : null;

            FuelMix? mix = null;
            if (genByDay.TryGetValue(day, out var g) && g.Count > 0)
            {
                // Energy-weighted daily mix: average each fuel across the hours it reported.
                var averages = g.Where(r => r.OutputMw is not null)
                    .GroupBy(r => r.Fuel)
                    .Select(x => (x.Key, x.Average(r => (double)r.OutputMw!.Value)));
                mix = FuelMixCalculator.FromAverages(averages, g.All(r => r.OutputMw is not null && r.MissingPoints == 0));
            }

            // Complete means every hour and 5-minute interval has been published. Missing metering
            // points within an hour are common (see FuelMix.Complete) and do not make a day partial.
            var complete = d is { Count: 24 }
                && p is { Count: 288 }
                && g is not null && g.Select(r => r.HourStartUtc).Distinct().Count() == 24;

            result.Add(new DailySummary(
                day,
                complete,
                peak?.OntarioDemandMw,
                peak?.HourStartUtc,
                avgDemand,
                avgPrice,
                mix is null ? null : Math.Round(mix.NonEmittingShare, 4),
                mix?.CarbonIntensity is { } ci ? Math.Round(ci, 1) : null));
        }

        return result;
    }

    private static DateTime TruncateToHour(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    private static DateTime MarketDayStartUtc(DateTime utc) =>
        MarketTime.HourStartUtc(MarketTime.FromUtc(utc).Date, 1);
}
