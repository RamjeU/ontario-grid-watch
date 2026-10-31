using GridWatch.Core.Domain;

namespace GridWatch.Infrastructure.Data;

// Persistence entities are kept separate from the Core records so the domain model
// does not depend on Entity Framework. All timestamps are stored in UTC.

public sealed class FuelOutputRow
{
    public DateTime HourStartUtc { get; set; }
    public Fuel Fuel { get; set; }
    public int? OutputMw { get; set; }
    public int MissingPoints { get; set; }

    public FuelOutputReading ToReading() => new(HourStartUtc, Fuel, OutputMw, MissingPoints);
}

public sealed class DemandRow
{
    public DateTime HourStartUtc { get; set; }
    public int OntarioDemandMw { get; set; }
    public int MarketDemandMw { get; set; }

    public DemandReading ToReading() => new(HourStartUtc, OntarioDemandMw, MarketDemandMw);
}

public sealed class PriceRow
{
    public DateTime IntervalStartUtc { get; set; }
    public decimal PricePerMwh { get; set; }

    public PriceReading ToReading() => new(IntervalStartUtc, PricePerMwh);
}

public sealed class LiveDemandRow
{
    public DateTime IntervalStartUtc { get; set; }
    public double OntarioDemandMw { get; set; }

    public LiveDemandReading ToReading() => new(IntervalStartUtc, OntarioDemandMw);
}

public enum IngestionSource
{
    FuelOutput,
    Demand,
    Realtime,
    SampleData,
}

public enum IngestionStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>An audit record of each ingestion attempt, shown on the status endpoint.</summary>
public sealed class IngestionRun
{
    public long Id { get; set; }
    public IngestionSource Source { get; set; }
    public IngestionStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int RowsInserted { get; set; }
    public int RowsUpdated { get; set; }
    public int Warnings { get; set; }
    public string? Error { get; set; }
}
