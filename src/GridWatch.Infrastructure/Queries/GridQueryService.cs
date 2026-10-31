using GridWatch.Core.Analytics;
using GridWatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GridWatch.Infrastructure.Queries;

/// <summary>
/// Read side. Queries are read-only (AsNoTracking) and fetch raw rows for a bounded range;
/// shaping happens in <see cref="GridAggregator"/>, which is unit-tested on its own.
/// </summary>
public sealed class GridQueryService(GridWatchDbContext db, TimeProvider clock)
{
    public async Task<GridSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var live = await db.LiveDemand.AsNoTracking()
            .OrderByDescending(d => d.IntervalStartUtc)
            .Select(d => new LiveDemandPoint(d.IntervalStartUtc, d.OntarioDemandMw))
            .FirstOrDefaultAsync(ct);

        var demand = await db.Demand.AsNoTracking()
            .OrderByDescending(d => d.HourStartUtc)
            .Select(d => new DemandPoint(d.HourStartUtc, d.OntarioDemandMw, d.MarketDemandMw))
            .FirstOrDefaultAsync(ct);

        var price = await db.Prices.AsNoTracking()
            .OrderByDescending(p => p.IntervalStartUtc)
            .Select(p => new PricePoint(p.IntervalStartUtc, p.PricePerMwh))
            .FirstOrDefaultAsync(ct);

        MixPoint? mix = null;
        var latestFuelHour = await db.FuelOutputs.AsNoTracking()
            .OrderByDescending(f => f.HourStartUtc)
            .Select(f => (DateTime?)f.HourStartUtc)
            .FirstOrDefaultAsync(ct);

        if (latestFuelHour is { } hour)
        {
            var rows = await db.FuelOutputs.AsNoTracking().Where(f => f.HourStartUtc == hour).ToListAsync(ct);
            mix = new MixPoint(hour, FuelMixCalculator.Calculate(rows.Select(r => r.ToReading())));
        }

        return new GridSnapshot(live, demand, price, mix, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<IReadOnlyList<RealtimePoint>> GetRealtimeAsync(TimeRange range, CancellationToken ct)
    {
        var demand = await db.LiveDemand.AsNoTracking()
            .Where(d => d.IntervalStartUtc >= range.FromUtc && d.IntervalStartUtc < range.ToUtc)
            .ToListAsync(ct);
        var prices = await db.Prices.AsNoTracking()
            .Where(p => p.IntervalStartUtc >= range.FromUtc && p.IntervalStartUtc < range.ToUtc)
            .ToListAsync(ct);
        return GridAggregator.Realtime(demand.Select(d => d.ToReading()), prices.Select(p => p.ToReading()));
    }

    public async Task<IReadOnlyList<GenerationPoint>> GetGenerationAsync(TimeRange range, Resolution resolution, CancellationToken ct)
    {
        var rows = await db.FuelOutputs.AsNoTracking()
            .Where(f => f.HourStartUtc >= range.FromUtc && f.HourStartUtc < range.ToUtc)
            .ToListAsync(ct);
        return GridAggregator.Generation(rows.Select(r => r.ToReading()), resolution);
    }

    public async Task<IReadOnlyList<DemandPoint>> GetDemandAsync(TimeRange range, CancellationToken ct) =>
        await db.Demand.AsNoTracking()
            .Where(d => d.HourStartUtc >= range.FromUtc && d.HourStartUtc < range.ToUtc)
            .OrderBy(d => d.HourStartUtc)
            .Select(d => new DemandPoint(d.HourStartUtc, d.OntarioDemandMw, d.MarketDemandMw))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HourlyPricePoint>> GetHourlyPricesAsync(TimeRange range, CancellationToken ct)
    {
        var rows = await db.Prices.AsNoTracking()
            .Where(p => p.IntervalStartUtc >= range.FromUtc && p.IntervalStartUtc < range.ToUtc)
            .ToListAsync(ct);
        return GridAggregator.HourlyPrices(rows.Select(r => r.ToReading()));
    }

    public async Task<IReadOnlyList<DailySummary>> GetDailySummaryAsync(TimeRange range, CancellationToken ct)
    {
        var demand = await db.Demand.AsNoTracking()
            .Where(d => d.HourStartUtc >= range.FromUtc && d.HourStartUtc < range.ToUtc).ToListAsync(ct);
        var prices = await db.Prices.AsNoTracking()
            .Where(p => p.IntervalStartUtc >= range.FromUtc && p.IntervalStartUtc < range.ToUtc).ToListAsync(ct);
        var fuel = await db.FuelOutputs.AsNoTracking()
            .Where(f => f.HourStartUtc >= range.FromUtc && f.HourStartUtc < range.ToUtc).ToListAsync(ct);

        return GridAggregator.DailySummaries(
            demand.Select(d => d.ToReading()),
            prices.Select(p => p.ToReading()),
            fuel.Select(f => f.ToReading()));
    }

    public async Task<IReadOnlyList<IngestionRun>> GetRecentRunsAsync(int count, CancellationToken ct) =>
        await db.IngestionRuns.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(count)
            .ToListAsync(ct);
}
