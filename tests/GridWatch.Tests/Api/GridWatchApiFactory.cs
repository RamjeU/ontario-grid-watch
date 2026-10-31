using GridWatch.Infrastructure.Ieso;
using GridWatch.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace GridWatch.Tests.Api;

/// <summary>
/// Hosts the real app in memory: real controllers, middleware, EF Core and SignalR, with a private
/// in-memory SQLite database seeded from the bundled sample, the background worker off, the clock
/// fixed to just after the sample period, and the IESO replaced by <see cref="FakeIesoHandler"/>.
/// </summary>
public sealed class GridWatchApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";

    // The sample covers market days 2026-10-17 to 2026-10-30; "now" is the end of that period.
    public static readonly DateTimeOffset Now = new(2026, 10, 31, 5, 0, 0, TimeSpan.Zero);

    private readonly string _database = $"file:gridwatch-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public GridWatchApiFactory()
    {
        // An in-memory SQLite database lives as long as one connection to it stays open.
        _keepAlive = new SqliteConnection($"Data Source={_database}");
        _keepAlive.Open();
    }

    public FakeIesoHandler Ieso { get; } = new();

    public FakeTimeProvider Clock { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:GridWatch", $"Data Source={_database}");
        builder.UseSetting("Ingestion:Enabled", "false");
        builder.UseSetting("Ingestion:SeedSampleData", "true");
        builder.UseSetting("Admin:ApiKey", AdminKey);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient<IesoReportClient>().ConfigurePrimaryHttpMessageHandler(() => Ieso);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive.Dispose();
        }
    }
}
