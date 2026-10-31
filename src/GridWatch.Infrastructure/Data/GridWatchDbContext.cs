using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GridWatch.Infrastructure.Data;

public sealed class GridWatchDbContext(DbContextOptions<GridWatchDbContext> options) : DbContext(options)
{
    public DbSet<FuelOutputRow> FuelOutputs => Set<FuelOutputRow>();
    public DbSet<DemandRow> Demand => Set<DemandRow>();
    public DbSet<PriceRow> Prices => Set<PriceRow>();
    public DbSet<LiveDemandRow> LiveDemand => Set<LiveDemandRow>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Natural keys make ingestion idempotent: re-reading a report updates rows instead of duplicating them.
        modelBuilder.Entity<FuelOutputRow>(e =>
        {
            e.ToTable("FuelOutput");
            e.HasKey(x => new { x.HourStartUtc, x.Fuel });
            e.Property(x => x.Fuel).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<DemandRow>(e =>
        {
            e.ToTable("Demand");
            e.HasKey(x => x.HourStartUtc);
        });

        modelBuilder.Entity<PriceRow>(e =>
        {
            e.ToTable("Price");
            e.HasKey(x => x.IntervalStartUtc);
            e.Property(x => x.PricePerMwh).HasPrecision(10, 2);
        });

        modelBuilder.Entity<LiveDemandRow>(e =>
        {
            e.ToTable("LiveDemand");
            e.HasKey(x => x.IntervalStartUtc);
        });

        modelBuilder.Entity<IngestionRun>(e =>
        {
            e.ToTable("IngestionRun");
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.StartedAtUtc);
        });

        // Databases drop DateTimeKind on the way back; mark every DateTime read as UTC.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(utc);
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(nullableUtc);
            }
        }
    }
}
