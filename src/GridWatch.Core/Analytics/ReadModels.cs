using GridWatch.Core.Domain;

namespace GridWatch.Core.Analytics;

/// <summary>A point-in-time view of the grid. Each source has its own "as of" time because the IESO
/// publishes them on different schedules: prices and live demand every 5 minutes, the hourly demand
/// report a few times a day, and fuel output once a day for the previous day.</summary>
public sealed record GridSnapshot(
    LiveDemandPoint? LiveDemand,
    DemandPoint? Demand,
    PricePoint? Price,
    MixPoint? Mix,
    DateTime GeneratedAtUtc);

public sealed record LiveDemandPoint(DateTime IntervalStartUtc, double OntarioDemandMw);

/// <summary>One 5-minute interval of real-time data. Either value can be missing for the newest interval.</summary>
public sealed record RealtimePoint(DateTime IntervalStartUtc, double? OntarioDemandMw, decimal? PricePerMwh);

public sealed record DemandPoint(DateTime HourStartUtc, int OntarioDemandMw, int MarketDemandMw);

public sealed record PricePoint(DateTime IntervalStartUtc, decimal PricePerMwh);

public sealed record MixPoint(DateTime HourStartUtc, FuelMix Mix);

/// <summary>Output for each fuel in one hour (or one day, when aggregated), in MW.</summary>
public sealed record GenerationPoint(DateTime StartUtc, IReadOnlyDictionary<Fuel, int?> OutputMw, bool Complete);

public sealed record HourlyPricePoint(DateTime HourStartUtc, decimal AveragePrice, decimal MinPrice, decimal MaxPrice, int Intervals);

/// <summary>
/// One market day's totals, used for the summary table. <paramref name="Complete"/> is false while
/// any source is missing hours or 5-minute intervals for that day, which is always true of today.
/// </summary>
public sealed record DailySummary(
    DateOnly MarketDate,
    bool Complete,
    int? PeakOntarioDemandMw,
    DateTime? PeakHourStartUtc,
    double? AverageOntarioDemandMw,
    decimal? AveragePrice,
    double? NonEmittingShare,
    double? CarbonIntensity);

public enum Resolution
{
    Hour,
    Day,
}
