using System.Globalization;
using GridWatch.Core.Domain;

namespace GridWatch.Core.Parsing;

/// <summary>
/// Parses the IESO "Generator Output by Fuel Type Hourly" report (PUB_GenOutputbyFuelHourly_YYYY.xml).
/// The yearly file is republished daily and holds every completed day so far.
/// </summary>
public static class GenOutputByFuelParser
{
    public const string DocId = "GenOutputbyFuelHourly";
    private const string ControlActions = "CONTROL ACTIONS";

    public static ParseResult<FuelOutputReading> Parse(Stream xml)
    {
        var ns = SafeXml.Ieso;
        var body = SafeXml.Body(SafeXml.Load(xml), DocId);
        var items = new List<FuelOutputReading>();
        var warnings = new List<string>();

        foreach (var day in body.Elements(ns + "DailyData"))
        {
            var dayText = (string?)day.Element(ns + "Day");
            if (!DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                warnings.Add($"Skipped a day with an unreadable date '{dayText}'.");
                continue;
            }

            foreach (var hour in day.Elements(ns + "HourlyData"))
            {
                if (!int.TryParse((string?)hour.Element(ns + "Hour"), out var hourEnding) || hourEnding is < 1 or > 24)
                {
                    warnings.Add($"Skipped an hour with an invalid number on {date:yyyy-MM-dd}.");
                    continue;
                }

                var hourStart = MarketTime.HourStartUtc(date, hourEnding);
                foreach (var fuelTotal in hour.Elements(ns + "FuelTotal"))
                {
                    var fuelName = (string?)fuelTotal.Element(ns + "Fuel");
                    if (string.Equals(fuelName?.Trim(), ControlActions, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Grid control actions (such as voltage reduction), not a generation fuel.
                    }

                    if (!FuelExtensions.TryParseIeso(fuelName, out var fuel))
                    {
                        warnings.Add($"Skipped unknown fuel '{fuelName}' on {date:yyyy-MM-dd} hour {hourEnding}.");
                        continue;
                    }

                    var value = fuelTotal.Element(ns + "EnergyValue");
                    var quality = int.TryParse((string?)value?.Element(ns + "OutputQuality"), out var q) ? q : 0;
                    int? output = int.TryParse((string?)value?.Element(ns + "Output"), out var mw) ? mw : null;

                    // OutputQuality is zero when complete and minus the number of missing data points otherwise.
                    items.Add(new FuelOutputReading(hourStart, fuel, output, Math.Abs(Math.Min(quality, 0))));
                }
            }
        }

        return new ParseResult<FuelOutputReading>(items, warnings);
    }
}
