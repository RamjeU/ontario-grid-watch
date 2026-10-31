namespace GridWatch.Core.Domain;

/// <summary>
/// The primary fuel categories the IESO reports generator output under.
/// </summary>
public enum Fuel
{
    Nuclear,
    Gas,
    Hydro,
    Wind,
    Solar,
    Biofuel,
    Other,
}

public static class FuelExtensions
{
    /// <summary>
    /// Maps the IESO's upper-case fuel names ("NUCLEAR", "GAS", ...) to <see cref="Fuel"/>.
    /// Unknown names return <c>false</c> rather than throwing, so a new fuel category in the
    /// report does not break ingestion.
    /// </summary>
    public static bool TryParseIeso(string? value, out Fuel fuel)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "NUCLEAR": fuel = Fuel.Nuclear; return true;
            case "GAS": fuel = Fuel.Gas; return true;
            case "HYDRO": fuel = Fuel.Hydro; return true;
            case "WIND": fuel = Fuel.Wind; return true;
            case "SOLAR": fuel = Fuel.Solar; return true;
            case "BIOFUEL": fuel = Fuel.Biofuel; return true;
            case "OTHER": fuel = Fuel.Other; return true;
            default: fuel = default; return false;
        }
    }

    /// <summary>
    /// Non-emitting sources at the point of generation. "Other" is excluded because the IESO
    /// uses it for a mix of resources (including storage) whose emissions are not reported.
    /// </summary>
    public static bool IsNonEmitting(this Fuel fuel) =>
        fuel is Fuel.Nuclear or Fuel.Hydro or Fuel.Wind or Fuel.Solar;
}
