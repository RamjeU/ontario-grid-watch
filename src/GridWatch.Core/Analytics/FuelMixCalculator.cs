using GridWatch.Core.Domain;

namespace GridWatch.Core.Analytics;

public sealed record FuelShare(Fuel Fuel, double OutputMw, double Share);

/// <summary>
/// The generation mix for one hour or an average over several.
/// <see cref="CarbonIntensity"/> is in grams CO2-equivalent per kWh and is null when there is no
/// output with a known emission factor.
/// </summary>
public sealed record FuelMix(
    double TotalMw,
    IReadOnlyList<FuelShare> Fuels,
    double NonEmittingShare,
    double? CarbonIntensity,
    bool Complete);

public static class FuelMixCalculator
{
    /// <summary>
    /// Builds the mix from readings for the same hour (or averages over a period).
    /// Fuels with no published output are left out of the totals and mark the mix incomplete,
    /// as do readings with missing metering points.
    /// </summary>
    public static FuelMix Calculate(IEnumerable<FuelOutputReading> readings)
    {
        var list = readings.ToList();
        var complete = list.Count > 0 && list.All(r => r.OutputMw is not null && r.MissingPoints == 0);

        var byFuel = list
            .Where(r => r.OutputMw is not null)
            .GroupBy(r => r.Fuel)
            .Select(g => (Fuel: g.Key, Mw: g.Average(r => (double)Math.Max(0, r.OutputMw!.Value))))
            .ToList();

        return Build(byFuel, complete);
    }

    /// <summary>Builds the mix from average output per fuel, for example a daily average.</summary>
    public static FuelMix FromAverages(IEnumerable<(Fuel Fuel, double Mw)> averages, bool complete) =>
        Build(averages.Select(a => (a.Fuel, Math.Max(0, a.Mw))).ToList(), complete);

    private static FuelMix Build(List<(Fuel Fuel, double Mw)> byFuel, bool complete)
    {
        var total = byFuel.Sum(f => f.Mw);
        var shares = byFuel
            .OrderBy(f => f.Fuel)
            .Select(f => new FuelShare(f.Fuel, f.Mw, total > 0 ? f.Mw / total : 0))
            .ToList();

        var nonEmitting = total > 0 ? byFuel.Where(f => f.Fuel.IsNonEmitting()).Sum(f => f.Mw) / total : 0;

        // Weighted average of emission factors over the fuels that have one.
        double weighted = 0, covered = 0;
        foreach (var (fuel, mw) in byFuel)
        {
            if (EmissionFactors.TryGet(fuel, out var factor))
            {
                weighted += factor * mw;
                covered += mw;
            }
        }

        double? intensity = covered > 0 ? weighted / covered : null;
        return new FuelMix(total, shares, nonEmitting, intensity, complete);
    }
}
