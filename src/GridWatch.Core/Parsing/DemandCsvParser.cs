using System.Globalization;
using GridWatch.Core.Domain;

namespace GridWatch.Core.Parsing;

/// <summary>
/// Parses the IESO hourly demand report (PUB_Demand_YYYY.csv). The file opens with a few
/// metadata lines starting with backslashes, then a header: Date,Hour,Market Demand,Ontario Demand.
/// </summary>
public static class DemandCsvParser
{
    public static ParseResult<DemandReading> Parse(TextReader reader)
    {
        var items = new List<DemandReading>();
        var warnings = new List<string>();
        var sawHeader = false;
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('\\'))
            {
                continue;
            }

            var cells = line.Split(',');
            if (!sawHeader)
            {
                if (cells.Length >= 4 && cells[0].Trim() == "Date" && cells[1].Trim() == "Hour")
                {
                    sawHeader = true;
                    continue;
                }

                throw new IesoFormatException($"Expected the header 'Date,Hour,Market Demand,Ontario Demand' but line {lineNumber} was '{line}'.");
            }

            if (cells.Length < 4
                || !DateOnly.TryParseExact(cells[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) || hour is < 1 or > 24
                || !int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var market)
                || !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ontario))
            {
                warnings.Add($"Skipped line {lineNumber}: '{line}'.");
                continue;
            }

            items.Add(new DemandReading(MarketTime.HourStartUtc(date, hour), ontario, market));
        }

        if (!sawHeader)
        {
            throw new IesoFormatException("Demand report has no header row.");
        }

        return new ParseResult<DemandReading>(items, warnings);
    }
}
