using System.ComponentModel.DataAnnotations;

namespace GridWatch.Infrastructure.Ingestion;

public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>Run the background worker that polls the IESO.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Load bundled sample data when the database is empty, so the app works offline.</summary>
    public bool SeedSampleData { get; set; } = true;

    [Url]
    public string BaseUrl { get; set; } = "https://reports-public.ieso.ca/public/";

    [Range(1, 60)]
    public int PollMinutes { get; set; } = 5;

    [Range(5, 1440)]
    public int DemandEveryMinutes { get; set; } = 15;

    [Range(15, 1440)]
    public int FuelOutputEveryMinutes { get; set; } = 60;

    /// <summary>How far back to keep fuel output and demand on each load.</summary>
    [Range(1, 366)]
    public int BackfillDays { get; set; } = 45;

    /// <summary>How many past hours of 5-minute prices and demand to fetch on startup (two requests per hour).</summary>
    [Range(1, 168)]
    public int RealtimeBackfillHours { get; set; } = 48;
}
