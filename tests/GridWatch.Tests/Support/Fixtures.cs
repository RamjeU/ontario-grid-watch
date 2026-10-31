using System.Text;

namespace GridWatch.Tests.Support;

internal static class Fixtures
{
    public static string PathTo(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static Stream Open(string name) => File.OpenRead(PathTo(name));

    public static Stream FromString(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    /// <summary>A minimal fuel report in the IESO format, with one hour per (day, hour, fuel, output, quality) tuple.</summary>
    public static string FuelReport(params (string Day, int Hour, string Fuel, string? Output, int Quality)[] rows)
    {
        var sb = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document docID="GenOutputbyFuelHourly" xmlns="http://www.ieso.ca/schema"><DocHeader/><DocBody><DeliveryYear>2026</DeliveryYear>
            """);
        foreach (var day in rows.GroupBy(r => r.Day))
        {
            sb.Append($"<DailyData><Day>{day.Key}</Day>");
            foreach (var hour in day.GroupBy(r => r.Hour))
            {
                sb.Append($"<HourlyData><Hour>{hour.Key}</Hour>");
                foreach (var r in hour)
                {
                    var output = r.Output is null ? "" : $"<Output>{r.Output}</Output>";
                    sb.Append($"<FuelTotal><Fuel>{r.Fuel}</Fuel><EnergyValue><OutputQuality>{r.Quality}</OutputQuality>{output}</EnergyValue></FuelTotal>");
                }

                sb.Append("</HourlyData>");
            }

            sb.Append("</DailyData>");
        }

        return sb.Append("</DocBody></Document>").ToString();
    }

    /// <summary>A real-time price report with the given (interval, price) pairs; other intervals are left empty, as in a running hour.</summary>
    public static string PriceReport(string date, int hour, params (int Interval, string Price)[] prices)
    {
        var sb = new StringBuilder($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document docID="RealtimeOntarioZonalPrice" xmlns="http://www.ieso.ca/schema"><DocHeader/><DocBody>
            <DeliveryDate>{date}</DeliveryDate><DeliveryHour>{hour}</DeliveryHour>
            """);
        for (var i = 1; i <= 12; i++)
        {
            var price = prices.FirstOrDefault(p => p.Interval == i).Price;
            sb.Append(price is null
                ? $"<ZonalPrice><Interval>{i}</Interval><Flag/><LmpCap/></ZonalPrice>"
                : $"<ZonalPrice><Interval>{i}</Interval><Flag>DSO-RD</Flag><LmpCap>{price}</LmpCap></ZonalPrice>");
        }

        return sb.Append("</DocBody></Document>").ToString();
    }

    /// <summary>A real-time totals report with ONTARIO DEMAND for the given intervals.</summary>
    public static string TotalsReport(string date, int hour, params (int Interval, string Demand)[] values)
    {
        var sb = new StringBuilder($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document docID="RealtimeTotals" xmlns="http://www.ieso.ca/schema"><DocHeader/><DocBody>
            <DeliveryDate>{date}</DeliveryDate><DeliveryHour>{hour}</DeliveryHour><Energies>
            """);
        foreach (var (interval, demand) in values)
        {
            sb.Append($"""
                <IntervalEnergy><Interval>{interval}</Interval>
                <MQ><MarketQuantity>Total Energy</MarketQuantity><EnergyMW>99999</EnergyMW></MQ>
                <MQ><MarketQuantity>ONTARIO DEMAND</MarketQuantity><EnergyMW>{demand}</EnergyMW></MQ>
                </IntervalEnergy>
                """);
        }

        return sb.Append("</Energies></DocBody></Document>").ToString();
    }
}
