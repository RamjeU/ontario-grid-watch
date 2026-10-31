using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ieso;
using GridWatch.Infrastructure.Ingestion;
using GridWatch.Infrastructure.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GridWatch.Infrastructure;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
}

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database, the IESO client and the ingestion services.
    /// Database:Provider picks SQLite (local development and tests) or SQL Server (Azure SQL in production).
    /// </summary>
    public static IServiceCollection AddGridWatchInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config.GetValue("Database:Provider", DatabaseProvider.Sqlite);
        var connection = config.GetConnectionString("GridWatch")
            ?? (provider == DatabaseProvider.Sqlite
                ? "Data Source=gridwatch.db"
                : throw new InvalidOperationException("ConnectionStrings:GridWatch is required for SQL Server."));

        services.AddDbContext<GridWatchDbContext>(options =>
        {
            if (provider == DatabaseProvider.SqlServer)
            {
                options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite(connection);
            }
        });

        services.AddOptions<IngestionOptions>()
            .Bind(config.GetSection(IngestionOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IesoReportClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
                http.BaseAddress = new Uri(options.BaseUrl);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OntarioGridWatch/1.0 (+https://github.com/RamjeU/ontario-grid-watch)");
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // The yearly fuel file is several MB; allow time for it on a slow link.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
            });

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<GridRepository>();
        services.AddScoped<GridQueryService>();
        services.AddScoped<IngestionService>();
        services.AddScoped<SampleDataSeeder>();

        if (config.GetValue($"{IngestionOptions.Section}:Enabled", true))
        {
            services.AddHostedService<IngestionWorker>();
        }

        return services;
    }

    /// <summary>
    /// Brings the schema up to date. SQL Server uses EF Core migrations; SQLite, used only for local
    /// development and tests, is created directly from the model.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GridWatchDbContext>();

        if (db.Database.IsSqlServer())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            // SQLite will not create missing folders, e.g. /home/data on Azure App Service.
            var file = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            var isFilePath = file != ":memory:" && !file.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
            if (isFilePath && Path.GetDirectoryName(Path.GetFullPath(file)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            await db.Database.EnsureCreatedAsync(ct);
        }

        var ingestion = scope.ServiceProvider.GetRequiredService<IOptions<IngestionOptions>>().Value;
        if (ingestion.SeedSampleData)
        {
            await scope.ServiceProvider.GetRequiredService<SampleDataSeeder>().SeedIfEmptyAsync(ct);
        }
    }
}
