using System.Globalization;
using GridWatch.Core.Domain;

namespace GridWatch.Core.Parsing;

/// <summary>
/// Parses the IESO "Realtime Totals" report (PUB_RealtimeTotals_YYYYMMDDHH.xml) and keeps the
/// ONTARIO DEMAND quantity for each 5-minute interval. Like the price report, the current hour's
/// file is published while the hour runs, with later intervals missing.
/// </summary>
public static class RealtimeTotalsParser
{
    public const string DocId = "RealtimeTotals";
    private const string OntarioDemand = "ONTARIO DEMAND";

    public static ParseResult<LiveDemandReading> Parse(Stream xml)
    {
        var ns = SafeXml.Ieso;
        var body = SafeXml.Body(SafeXml.Load(xml), DocId);
        var warnings = new List<string>();

        var dateText = (string?)body.Element(ns + "DeliveryDate");
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new IesoFormatException($"Totals report has an unreadable delivery date '{dateText}'.");
        }

        if (!int.TryParse((string?)body.Element(ns + "DeliveryHour"), out var hour) || hour is < 1 or > 24)
        {
            throw new IesoFormatException("Totals report has an invalid delivery hour.");
        }

        var items = new List<LiveDemandReading>();
        var intervals = body.Element(ns + "Energies")?.Elements(ns + "IntervalEnergy") ?? [];
        foreach (var energy in intervals)
        {
            if (!int.TryParse((string?)energy.Element(ns + "Interval"), out var interval) || interval is < 1 or > 12)
            {
                warnings.Add($"Skipped an interval with an invalid number in {date:yyyy-MM-dd} hour {hour}.");
                continue;
            }

            var demand = energy.Elements(ns + "MQ")
                .FirstOrDefault(mq => string.Equals(((string?)mq.Element(ns + "MarketQuantity"))?.Trim(), OntarioDemand, StringComparison.OrdinalIgnoreCase));
            var text = (string?)demand?.Element(ns + "EnergyMW");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue; // Not yet published.
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mw))
            {
                warnings.Add($"Skipped interval {interval} with an unreadable demand '{text}'.");
                continue;
            }

            items.Add(new LiveDemandReading(MarketTime.IntervalStartUtc(date, hour, interval), mw));
        }

        return new ParseResult<LiveDemandReading>(items, warnings);
    }
}
