using GridWatch.Core.Domain;

namespace GridWatch.Core.Analytics;

/// <summary>
/// Median life-cycle greenhouse gas emissions per kWh generated, from IPCC AR5 Working Group III,
/// Annex III, Table A.III.2 (2014). These are global medians, not Ontario-specific measurements,
/// so the carbon intensity this app shows is an estimate.
/// </summary>
public static class EmissionFactors
{
    private static readonly IReadOnlyDictionary<Fuel, double> GramsPerKwh = new Dictionary<Fuel, double>
    {
        [Fuel.Nuclear] = 12,
        [Fuel.Gas] = 490,     // Combined cycle. Ontario's simple-cycle peakers emit more, so this is a lower bound.
        [Fuel.Hydro] = 24,
        [Fuel.Wind] = 11,     // Onshore.
        [Fuel.Solar] = 48,    // Utility-scale PV.
        [Fuel.Biofuel] = 230, // Dedicated biomass.
        // Fuel.Other has no single factor and is excluded from the estimate.
    };

    public static bool TryGet(Fuel fuel, out double gramsPerKwh) => GramsPerKwh.TryGetValue(fuel, out gramsPerKwh);
}
