using GridWatch.Core.Domain;
using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ieso;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridWatch.Infrastructure.Ingestion;

public sealed record IngestionSummary(IngestionSource Source, IngestionStatus Status, int Inserted, int Updated, int Warnings, string? Error);

/// <summary>
/// Pulls each IESO report, parses it and upserts the rows. Every attempt is recorded as an
/// <see cref="IngestionRun"/>, so failures are visible through the API rather than only in logs.
/// </summary>
public sealed class IngestionService(
    IesoReportClient client,
    GridRepository repository,
    GridWatchDbContext db,
    IOptions<IngestionOptions> options,
    TimeProvider clock,
    ILogger<IngestionService> logger)
{
    private readonly IngestionOptions _options = options.Value;

    public Task<IngestionSummary> IngestFuelOutputAsync(CancellationToken ct) =>
        RecordAsync(IngestionSource.FuelOutput, async () =>
        {
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-_options.BackfillDays);
            var total = default(UpsertResult);
            var warnings = 0;

            foreach (var year in YearsBetween(cutoff, clock.GetUtcNow().UtcDateTime))
            {
                var parsed = await client.GetFuelOutputAsync(year, ct);
                warnings += parsed.Warnings.Count;
                total += await repository.UpsertFuelOutputAsync(parsed.Items.Where(r => r.HourStartUtc >= cutoff).ToList(), ct);
            }

            return (total, warnings);
        }, ct);

    public Task<IngestionSummary> IngestDemandAsync(CancellationToken ct) =>
        RecordAsync(IngestionSource.Demand, async () =>
        {
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-_options.BackfillDays);
            var total = default(UpsertResult);
            var warnings = 0;

            foreach (var year in YearsBetween(cutoff, clock.GetUtcNow().UtcDateTime))
            {
                var parsed = await client.GetDemandAsync(year, ct);
                warnings += parsed.Warnings.Count;
                total += await repository.UpsertDemandAsync(parsed.Items.Where(r => r.HourStartUtc >= cutoff).ToList(), ct);
            }

            return (total, warnings);
        }, ct);

    /// <summary>
    /// Fetches 5-minute prices and Ontario demand for the last <paramref name="hours"/> market hours,
    /// skipping hours that are already complete in the database. The IESO publishes one file per hour
    /// for each, so this is the chattiest source.
    /// </summary>
    public Task<IngestionSummary> IngestRealtimeAsync(int hours, CancellationToken ct) =>
        RecordAsync(IngestionSource.Realtime, async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var from = currentHour.AddHours(-(hours - 1));
            var complete = await repository.CompleteRealtimeHoursAsync(from, currentHour.AddHours(1), ct);

            var total = default(UpsertResult);
            var warnings = 0;
            for (var hourStart = from; hourStart <= currentHour; hourStart = hourStart.AddHours(1))
            {
                if (complete.Contains(hourStart))
                {
                    continue;
                }

                var (date, hourEnding) = MarketTime.FromUtc(hourStart);

                // Either file can be missing for an hour that has not been published yet.
                if (await client.GetZonalPricesAsync(date, hourEnding, ct) is { } prices)
                {
                    warnings += prices.Warnings.Count;
                    total += await repository.UpsertPricesAsync(prices.Items, ct);
                }

                if (await client.GetRealtimeTotalsAsync(date, hourEnding, ct) is { } demand)
                {
                    warnings += demand.Warnings.Count;
                    total += await repository.UpsertLiveDemandAsync(demand.Items, ct);
                }
            }

            return (total, warnings);
        }, ct);

    private async Task<IngestionSummary> RecordAsync(
        IngestionSource source,
        Func<Task<(UpsertResult Result, int Warnings)>> work,
        CancellationToken ct)
    {
        var run = new IngestionRun { Source = source, Status = IngestionStatus.Running, StartedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            var (result, warnings) = await work();
            run.Status = IngestionStatus.Succeeded;
            run.RowsInserted = result.Inserted;
            run.RowsUpdated = result.Updated;
            run.Warnings = warnings;
            logger.LogInformation("Ingested {Source}: {Inserted} inserted, {Updated} updated, {Warnings} warnings",
                source, result.Inserted, result.Updated, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = IngestionStatus.Failed;
            run.Error = Truncate(ex.Message, 2000);
            logger.LogWarning(ex, "Ingestion of {Source} failed", source);
        }

        run.FinishedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.IngestionRuns.Update(run);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        return new IngestionSummary(source, run.Status, run.RowsInserted, run.RowsUpdated, run.Warnings, run.Error);
    }

    private static IEnumerable<int> YearsBetween(DateTime fromUtc, DateTime toUtc)
    {
        // Report files are per market year, which follows EST, so convert before taking the year.
        var first = MarketTime.FromUtc(fromUtc).Date.Year;
        var last = MarketTime.FromUtc(toUtc).Date.Year;
        return Enumerable.Range(first, Math.Max(1, last - first + 1));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
