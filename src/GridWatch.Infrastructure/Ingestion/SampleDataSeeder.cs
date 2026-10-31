using System.Globalization;
using GridWatch.Core.Domain;
using GridWatch.Core.Parsing;
using GridWatch.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace GridWatch.Infrastructure.Ingestion;

/// <summary>
/// Loads two weeks of real IESO data bundled with the app when the database is empty, so the
/// dashboard works offline, in tests and on first start before the worker has fetched anything.
/// The fuel and demand samples are trimmed copies of the IESO files and go through the same parsers.
/// </summary>
public sealed class SampleDataSeeder(GridRepository repository, GridWatchDbContext db, TimeProvider clock, ILogger<SampleDataSeeder> logger)
{
    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct)
    {
        if (!await repository.IsEmptyAsync(ct))
        {
            return false;
        }

        var started = clock.GetUtcNow().UtcDateTime;
        await using var fuelStream = Open("PUB_GenOutputbyFuelHourly_sample.xml");
        var fuel = GenOutputByFuelParser.Parse(fuelStream);

        using var demandReader = new StreamReader(Open("PUB_Demand_sample.csv"));
        var demand = DemandCsvParser.Parse(demandReader);

        using var realtimeReader = new StreamReader(Open("realtime_sample.csv"));
        var (prices, liveDemand) = ReadRealtime(realtimeReader);

        var result = await repository.UpsertFuelOutputAsync(fuel.Items, ct)
            + await repository.UpsertDemandAsync(demand.Items, ct)
            + await repository.UpsertPricesAsync(prices, ct)
            + await repository.UpsertLiveDemandAsync(liveDemand, ct);

        db.IngestionRuns.Add(new IngestionRun
        {
            Source = IngestionSource.SampleData,
            Status = IngestionStatus.Succeeded,
            StartedAtUtc = started,
            FinishedAtUtc = clock.GetUtcNow().UtcDateTime,
            RowsInserted = result.Inserted,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Rows} rows of bundled sample data", result.Inserted);
        return true;
    }

    /// <summary>
    /// The real-time sample is flattened to IntervalStartUtc,PricePerMwh,OntarioDemandMw rather than
    /// shipping hundreds of hourly XML files. The script that built it ran the same fields through
    /// the IESO's per-hour reports.
    /// </summary>
    private static (List<PriceReading> Prices, List<LiveDemandReading> Demand) ReadRealtime(TextReader reader)
    {
        var prices = new List<PriceReading>();
        var demand = new List<LiveDemandReading>();
        reader.ReadLine(); // header
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split(',');
            if (cells.Length != 3
                || !DateTime.TryParse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
            {
                continue;
            }

            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            if (decimal.TryParse(cells[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                prices.Add(new PriceReading(start, price));
            }

            if (double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mw))
            {
                demand.Add(new LiveDemandReading(start, mw));
            }
        }

        return (prices, demand);
    }

    private static Stream Open(string name) =>
        typeof(SampleDataSeeder).Assembly.GetManifestResourceStream($"GridWatch.Infrastructure.SampleData.{name}")
        ?? throw new InvalidOperationException($"Embedded sample file '{name}' is missing.");
}
