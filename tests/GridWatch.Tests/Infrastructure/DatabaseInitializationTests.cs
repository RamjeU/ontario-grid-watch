using GridWatch.Infrastructure;
using GridWatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridWatch.Tests.Infrastructure;

public sealed class DatabaseInitializationTests
{
    [Fact]
    public async Task SQLite_mode_creates_a_missing_data_folder_and_the_schema()
    {
        // Mirrors the Azure free mode, where the database lives in a folder under /home that may not exist yet.
        var root = Path.Combine(Path.GetTempPath(), $"gridwatch-{Guid.NewGuid():N}");
        var file = Path.Combine(root, "data", "gridwatch.db");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:GridWatch"] = $"Data Source={file};Pooling=False",
            ["Ingestion:Enabled"] = "false",
            ["Ingestion:SeedSampleData"] = "true",
        }).Build();

        var services = new ServiceCollection().AddLogging(b => b.ClearProviders());
        services.AddGridWatchInfrastructure(config);
        await using var provider = services.BuildServiceProvider();

        try
        {
            await provider.InitializeDatabaseAsync();

            Assert.True(File.Exists(file));
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GridWatchDbContext>();
            Assert.Equal(14 * 24, await db.Demand.CountAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
