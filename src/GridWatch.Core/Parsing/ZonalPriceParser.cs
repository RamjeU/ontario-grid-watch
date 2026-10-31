using System.Globalization;
using GridWatch.Core.Domain;

namespace GridWatch.Core.Parsing;

/// <summary>
/// Parses the IESO "Real-Time 5-min Ontario Zonal Energy Price" report
/// (PUB_RealtimeOntarioZonalPrice_YYYYMMDDHH.xml). The file for the current hour is published
/// while the hour is still running, so later intervals are present but empty.
/// </summary>
public static class ZonalPriceParser
{
    public const string DocId = "RealtimeOntarioZonalPrice";

    public static ParseResult<PriceReading> Parse(Stream xml)
    {
        var ns = SafeXml.Ieso;
        var body = SafeXml.Body(SafeXml.Load(xml), DocId);
        var warnings = new List<string>();

        var dateText = (string?)body.Element(ns + "DeliveryDate");
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new IesoFormatException($"Price report has an unreadable delivery date '{dateText}'.");
        }

        if (!int.TryParse((string?)body.Element(ns + "DeliveryHour"), out var hour) || hour is < 1 or > 24)
        {
            throw new IesoFormatException("Price report has an invalid delivery hour.");
        }

        var items = new List<PriceReading>();
        foreach (var zonal in body.Elements(ns + "ZonalPrice"))
        {
            if (!int.TryParse((string?)zonal.Element(ns + "Interval"), out var interval) || interval is < 1 or > 12)
            {
                warnings.Add($"Skipped an interval with an invalid number in {date:yyyy-MM-dd} hour {hour}.");
                continue;
            }

            var priceText = (string?)zonal.Element(ns + "LmpCap");
            if (string.IsNullOrWhiteSpace(priceText))
            {
                continue; // Not yet published.
            }

            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                warnings.Add($"Skipped interval {interval} with an unreadable price '{priceText}'.");
                continue;
            }

            items.Add(new PriceReading(MarketTime.IntervalStartUtc(date, hour, interval), price));
        }

        return new ParseResult<PriceReading>(items, warnings);
    }
}
