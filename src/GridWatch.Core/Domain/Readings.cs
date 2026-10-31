namespace GridWatch.Core.Domain;

/// <summary>
/// Hourly output for one fuel. <paramref name="OutputMw"/> is null when the IESO published no
/// value; <paramref name="MissingPoints"/> is how many metering points were unavailable
/// (the report's OutputQuality flag, stored as a positive count).
/// </summary>
public sealed record FuelOutputReading(DateTime HourStartUtc, Fuel Fuel, int? OutputMw, int MissingPoints);

/// <summary>
/// Hourly demand. Ontario demand is what Ontario consumers used; market demand also includes exports.
/// </summary>
public sealed record DemandReading(DateTime HourStartUtc, int OntarioDemandMw, int MarketDemandMw);

/// <summary>A 5-minute real-time Ontario Zonal Price, in dollars per megawatt-hour.</summary>
public sealed record PriceReading(DateTime IntervalStartUtc, decimal PricePerMwh);

/// <summary>5-minute real-time Ontario demand in MW, from the IESO Realtime Totals report.</summary>
public sealed record LiveDemandReading(DateTime IntervalStartUtc, double OntarioDemandMw);
