using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ingestion;
using GridWatch.Infrastructure.Queries;
using GridWatch.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GridWatch.Tests.Infrastructure;

/// <summary>Runs only when GRIDWATCH_SQLSERVER is set to a connection string (CI provides a SQL Server container).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "GRIDWATCH_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a SQL Server connection string to run.";
        }
    }
}

/// <summary>
/// Proves the production path: the EF Core migration applies to a real SQL Server, and seeding,
/// upserts and queries behave as they do on SQLite.
/// </summary>
public sealed class SqlServerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 31, 5, 0, 0, TimeSpan.Zero));

    [SqlServerFact]
    public async Task Migration_applies_and_the_app_works_end_to_end_on_SQL_Server()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerFactAttribute.Variable)!;
        var database = $"GridWatchTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<GridWatchDbContext>()
            .UseSqlServer($"{connection};Database={database}")
            .Options;

        await using var db = new GridWatchDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var seeder = new SampleDataSeeder(new GridRepository(db), db, _clock, NullLogger<SampleDataSeeder>.Instance);
            Assert.True(await seeder.SeedIfEmptyAsync(CancellationToken.None));

            // Re-applying the same rows through the repository changes nothing.
            var repository = new GridRepository(db);
            var rows = await db.Prices.AsNoTracking().Select(p => p.ToReading()).ToListAsync();
            var again = await repository.UpsertPricesAsync(rows, CancellationToken.None);
            Assert.Equal(default, again);

            var snapshot = await new GridQueryService(db, _clock).GetSnapshotAsync(CancellationToken.None);
            Assert.Equal(new DateTime(2026, 10, 31, 4, 55, 0, DateTimeKind.Utc), snapshot.Price!.IntervalStartUtc);
            Assert.Equal(DateTimeKind.Utc, snapshot.Price.IntervalStartUtc.Kind);
            Assert.Equal(7, snapshot.Mix!.Mix.Fuels.Count);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
