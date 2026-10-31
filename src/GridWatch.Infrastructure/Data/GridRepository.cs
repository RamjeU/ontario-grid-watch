using GridWatch.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace GridWatch.Infrastructure.Data;

public readonly record struct UpsertResult(int Inserted, int Updated)
{
    public static UpsertResult operator +(UpsertResult a, UpsertResult b) => new(a.Inserted + b.Inserted, a.Updated + b.Updated);
}

/// <summary>
/// Write side. Each upsert loads the existing rows for the incoming time window once, then inserts
/// what is new and updates only what changed. The IESO revises recent data, so updates matter.
/// This works the same on SQLite and SQL Server; a production system with much larger batches
/// would use a provider-specific bulk MERGE instead.
/// </summary>
public sealed class GridRepository(GridWatchDbContext db)
{
    public async Task<UpsertResult> UpsertFuelOutputAsync(IReadOnlyCollection<FuelOutputReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0)
        {
            return default;
        }

        var (min, max) = (readings.Min(r => r.HourStartUtc), readings.Max(r => r.HourStartUtc));
        var existing = await db.FuelOutputs
            .Where(r => r.HourStartUtc >= min && r.HourStartUtc <= max)
            .ToDictionaryAsync(r => (r.HourStartUtc, r.Fuel), ct);

        int inserted = 0, updated = 0;
        foreach (var reading in readings.DistinctBy(r => (r.HourStartUtc, r.Fuel)))
        {
            if (existing.TryGetValue((reading.HourStartUtc, reading.Fuel), out var row))
            {
                if (row.OutputMw != reading.OutputMw || row.MissingPoints != reading.MissingPoints)
                {
                    row.OutputMw = reading.OutputMw;
                    row.MissingPoints = reading.MissingPoints;
                    updated++;
                }
            }
            else
            {
                db.FuelOutputs.Add(new FuelOutputRow
                {
                    HourStartUtc = reading.HourStartUtc,
                    Fuel = reading.Fuel,
                    OutputMw = reading.OutputMw,
                    MissingPoints = reading.MissingPoints,
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new UpsertResult(inserted, updated);
    }

    public async Task<UpsertResult> UpsertDemandAsync(IReadOnlyCollection<DemandReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0)
        {
            return default;
        }

        var (min, max) = (readings.Min(r => r.HourStartUtc), readings.Max(r => r.HourStartUtc));
        var existing = await db.Demand
            .Where(r => r.HourStartUtc >= min && r.HourStartUtc <= max)
            .ToDictionaryAsync(r => r.HourStartUtc, ct);

        int inserted = 0, updated = 0;
        foreach (var reading in readings.DistinctBy(r => r.HourStartUtc))
        {
            if (existing.TryGetValue(reading.HourStartUtc, out var row))
            {
                if (row.OntarioDemandMw != reading.OntarioDemandMw || row.MarketDemandMw != reading.MarketDemandMw)
                {
                    row.OntarioDemandMw = reading.OntarioDemandMw;
                    row.MarketDemandMw = reading.MarketDemandMw;
                    updated++;
                }
            }
            else
            {
                db.Demand.Add(new DemandRow
                {
                    HourStartUtc = reading.HourStartUtc,
                    OntarioDemandMw = reading.OntarioDemandMw,
                    MarketDemandMw = reading.MarketDemandMw,
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new UpsertResult(inserted, updated);
    }

    public async Task<UpsertResult> UpsertPricesAsync(IReadOnlyCollection<PriceReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0)
        {
            return default;
        }

        var (min, max) = (readings.Min(r => r.IntervalStartUtc), readings.Max(r => r.IntervalStartUtc));
        var existing = await db.Prices
            .Where(r => r.IntervalStartUtc >= min && r.IntervalStartUtc <= max)
            .ToDictionaryAsync(r => r.IntervalStartUtc, ct);

        int inserted = 0, updated = 0;
        foreach (var reading in readings.DistinctBy(r => r.IntervalStartUtc))
        {
            if (existing.TryGetValue(reading.IntervalStartUtc, out var row))
            {
                if (row.PricePerMwh != reading.PricePerMwh)
                {
                    row.PricePerMwh = reading.PricePerMwh;
                    updated++;
                }
            }
            else
            {
                db.Prices.Add(new PriceRow { IntervalStartUtc = reading.IntervalStartUtc, PricePerMwh = reading.PricePerMwh });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new UpsertResult(inserted, updated);
    }

    public async Task<UpsertResult> UpsertLiveDemandAsync(IReadOnlyCollection<LiveDemandReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0)
        {
            return default;
        }

        var (min, max) = (readings.Min(r => r.IntervalStartUtc), readings.Max(r => r.IntervalStartUtc));
        var existing = await db.LiveDemand
            .Where(r => r.IntervalStartUtc >= min && r.IntervalStartUtc <= max)
            .ToDictionaryAsync(r => r.IntervalStartUtc, ct);

        int inserted = 0, updated = 0;
        foreach (var reading in readings.DistinctBy(r => r.IntervalStartUtc))
        {
            if (existing.TryGetValue(reading.IntervalStartUtc, out var row))
            {
                if (Math.Abs(row.OntarioDemandMw - reading.OntarioDemandMw) > 0.001)
                {
                    row.OntarioDemandMw = reading.OntarioDemandMw;
                    updated++;
                }
            }
            else
            {
                db.LiveDemand.Add(new LiveDemandRow { IntervalStartUtc = reading.IntervalStartUtc, OntarioDemandMw = reading.OntarioDemandMw });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new UpsertResult(inserted, updated);
    }

    /// <summary>
    /// Hours in the window that already have all twelve 5-minute prices and demand values,
    /// so they need no refetch.
    /// </summary>
    public async Task<HashSet<DateTime>> CompleteRealtimeHoursAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var prices = await db.Prices
            .Where(p => p.IntervalStartUtc >= fromUtc && p.IntervalStartUtc < toUtc)
            .Select(p => p.IntervalStartUtc)
            .ToListAsync(ct);
        var demand = await db.LiveDemand
            .Where(d => d.IntervalStartUtc >= fromUtc && d.IntervalStartUtc < toUtc)
            .Select(d => d.IntervalStartUtc)
            .ToListAsync(ct);

        var completePrices = CompleteHours(prices);
        completePrices.IntersectWith(CompleteHours(demand));
        return completePrices;
    }

    private static HashSet<DateTime> CompleteHours(IEnumerable<DateTime> intervalStarts) =>
        intervalStarts
            .GroupBy(s => new DateTime(s.Year, s.Month, s.Day, s.Hour, 0, 0, DateTimeKind.Utc))
            .Where(g => g.Count() >= 12)
            .Select(g => g.Key)
            .ToHashSet();

    public async Task<bool> IsEmptyAsync(CancellationToken ct) =>
        !await db.Demand.AnyAsync(ct) && !await db.FuelOutputs.AnyAsync(ct) && !await db.Prices.AnyAsync(ct);
}
