using System.Net;
using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ieso;
using GridWatch.Infrastructure.Ingestion;
using GridWatch.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GridWatch.Tests.Infrastructure;

public sealed class IngestionServiceTests : IDisposable
{
    // 2026-10-31 12:20 EST, inside market hour 13.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 31, 17, 20, 0, TimeSpan.Zero));
    private readonly FakeIesoHandler _ieso = new();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly GridWatchDbContext _db;
    private readonly IngestionService _service;

    public IngestionServiceTests()
    {
        _connection.Open();
        _db = new GridWatchDbContext(new DbContextOptionsBuilder<GridWatchDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var client = new IesoReportClient(new HttpClient(_ieso) { BaseAddress = new Uri("https://ieso.test/public/") });
        var options = Options.Create(new IngestionOptions { BackfillDays = 30 });
        _service = new IngestionService(client, new GridRepository(_db), _db, options, _clock, NullLogger<IngestionService>.Instance);
    }

    [Fact]
    public async Task Ingesting_the_same_report_twice_is_idempotent()
    {
        _ieso.Serve("GenOutputbyFuelHourly/PUB_GenOutputbyFuelHourly_2026.xml", File.ReadAllText(Fixtures.PathTo("PUB_GenOutputbyFuelHourly_sample.xml")));

        var first = await _service.IngestFuelOutputAsync(CancellationToken.None);
        var second = await _service.IngestFuelOutputAsync(CancellationToken.None);

        Assert.Equal(IngestionStatus.Succeeded, first.Status);
        Assert.Equal(14 * 24 * 7, first.Inserted);
        Assert.Equal((0, 0), (second.Inserted, second.Updated));
        Assert.Equal(14 * 24 * 7, await _db.FuelOutputs.CountAsync());
    }

    [Fact]
    public async Task A_revised_value_updates_the_existing_row()
    {
        const string path = "GenOutputbyFuelHourly/PUB_GenOutputbyFuelHourly_2026.xml";
        _ieso.Serve(path, Fixtures.FuelReport(("2026-10-30", 1, "WIND", "1000", -1)));
        await _service.IngestFuelOutputAsync(CancellationToken.None);

        _ieso.Serve(path, Fixtures.FuelReport(("2026-10-30", 1, "WIND", "1042", 0)));
        var revised = await _service.IngestFuelOutputAsync(CancellationToken.None);

        Assert.Equal((0, 1), (revised.Inserted, revised.Updated));
        var row = await _db.FuelOutputs.SingleAsync();
        Assert.Equal(1042, row.OutputMw);
        Assert.Equal(0, row.MissingPoints);
    }

    [Fact]
    public async Task Data_older_than_the_backfill_window_is_not_stored()
    {
        _ieso.Serve("GenOutputbyFuelHourly/PUB_GenOutputbyFuelHourly_2026.xml", Fixtures.FuelReport(
            ("2026-01-01", 1, "NUCLEAR", "9000", 0),
            ("2026-10-30", 1, "NUCLEAR", "7000", 0)));

        await _service.IngestFuelOutputAsync(CancellationToken.None);

        Assert.Equal(7000, (await _db.FuelOutputs.SingleAsync()).OutputMw);
    }

    [Fact]
    public async Task Realtime_ingestion_skips_unpublished_hours_and_complete_hours()
    {
        // Only the current hour (HE13 on 2026-10-31) is published, with three intervals so far.
        _ieso.Serve("RealtimeOntarioZonalPrice/PUB_RealtimeOntarioZonalPrice_2026103113.xml",
            Fixtures.PriceReport("2026-10-31", 13, (1, "50.10"), (2, "51.20"), (3, "49.90")));
        _ieso.Serve("RealtimeTotals/PUB_RealtimeTotals_2026103113.xml",
            Fixtures.TotalsReport("2026-10-31", 13, (1, "15000"), (2, "15010"), (3, "15020")));

        var result = await _service.IngestRealtimeAsync(hours: 3, CancellationToken.None);

        Assert.Equal(IngestionStatus.Succeeded, result.Status);
        Assert.Equal(6, result.Inserted);
        Assert.Equal(3, await _db.Prices.CountAsync());
        Assert.Equal(3, await _db.LiveDemand.CountAsync());
        Assert.Equal(6, _ieso.Requests.Count); // 3 hours x 2 reports; the two earlier hours 404

        // Fill an earlier hour completely; the next run should not request it again.
        var full = Enumerable.Range(1, 12).Select(i => (i, "40.00")).ToArray();
        _ieso.Serve("RealtimeOntarioZonalPrice/PUB_RealtimeOntarioZonalPrice_2026103112.xml", Fixtures.PriceReport("2026-10-31", 12, full));
        _ieso.Serve("RealtimeTotals/PUB_RealtimeTotals_2026103112.xml", Fixtures.TotalsReport("2026-10-31", 12, full));
        await _service.IngestRealtimeAsync(hours: 2, CancellationToken.None);
        _ieso.Requests.Clear();

        await _service.IngestRealtimeAsync(hours: 2, CancellationToken.None);

        Assert.DoesNotContain(_ieso.Requests, r => r.Contains("2026103112"));
        Assert.Contains(_ieso.Requests, r => r.Contains("2026103113"));
    }

    [Fact]
    public async Task Failures_are_recorded_as_failed_runs_instead_of_throwing()
    {
        _ieso.ForceStatus = HttpStatusCode.InternalServerError;

        var result = await _service.IngestDemandAsync(CancellationToken.None);

        Assert.Equal(IngestionStatus.Failed, result.Status);
        var run = await _db.IngestionRuns.SingleAsync();
        Assert.Equal(IngestionSource.Demand, run.Source);
        Assert.Equal(IngestionStatus.Failed, run.Status);
        Assert.Contains("500", run.Error);
        Assert.NotNull(run.FinishedAtUtc);
    }

    [Fact]
    public async Task Bundled_sample_data_seeds_an_empty_database_once()
    {
        var seeder = new SampleDataSeeder(new GridRepository(_db), _db, _clock, NullLogger<SampleDataSeeder>.Instance);

        Assert.True(await seeder.SeedIfEmptyAsync(CancellationToken.None));
        Assert.False(await seeder.SeedIfEmptyAsync(CancellationToken.None));

        Assert.Equal(14 * 24, await _db.Demand.CountAsync());
        Assert.Equal(14 * 288, await _db.Prices.CountAsync());
        Assert.Equal(14 * 288, await _db.LiveDemand.CountAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
