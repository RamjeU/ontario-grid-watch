using GridWatch.Core.Domain;
using GridWatch.Core.Parsing;
using GridWatch.Tests.Support;

namespace GridWatch.Tests.Core;

public class GenOutputByFuelParserTests
{
    [Fact]
    public void Parses_two_weeks_of_real_IESO_data()
    {
        using var stream = Fixtures.Open("PUB_GenOutputbyFuelHourly_sample.xml");

        var result = GenOutputByFuelParser.Parse(stream);

        Assert.Equal(14 * 24 * 7, result.Items.Count); // days x hours x fuels
        Assert.Empty(result.Warnings);
        Assert.All(result.Items, r => Assert.Equal(DateTimeKind.Utc, r.HourStartUtc.Kind));
        Assert.Equal(7, result.Items.Select(r => r.Fuel).Distinct().Count());
    }

    [Fact]
    public void Reads_output_and_turns_the_quality_flag_into_a_missing_point_count()
    {
        var xml = Fixtures.FuelReport(
            ("2026-01-01", 1, "NUCLEAR", "9602", 0),
            ("2026-01-01", 1, "GAS", "3246", -2));

        var items = GenOutputByFuelParser.Parse(Fixtures.FromString(xml)).Items;

        var nuclear = Assert.Single(items, r => r.Fuel == Fuel.Nuclear);
        Assert.Equal(9602, nuclear.OutputMw);
        Assert.Equal(0, nuclear.MissingPoints);
        Assert.Equal(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc), nuclear.HourStartUtc);
        Assert.Equal(2, Assert.Single(items, r => r.Fuel == Fuel.Gas).MissingPoints);
    }

    [Fact]
    public void Missing_output_becomes_null_rather_than_zero()
    {
        var xml = Fixtures.FuelReport(("2026-01-01", 1, "WIND", null, -5));

        var reading = Assert.Single(GenOutputByFuelParser.Parse(Fixtures.FromString(xml)).Items);

        Assert.Null(reading.OutputMw);
    }

    [Fact]
    public void Skips_control_actions_silently_and_warns_on_unknown_fuels()
    {
        var xml = Fixtures.FuelReport(
            ("2026-01-01", 1, "CONTROL ACTIONS", "0", 0),
            ("2026-01-01", 1, "FUSION", "100", 0),
            ("2026-01-01", 1, "HYDRO", "3000", 0));

        var result = GenOutputByFuelParser.Parse(Fixtures.FromString(xml));

        Assert.Equal(Fuel.Hydro, Assert.Single(result.Items).Fuel);
        Assert.Contains("FUSION", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Rejects_a_different_report_type()
    {
        var xml = Fixtures.PriceReport("2026-01-01", 1);

        var ex = Assert.Throws<IesoFormatException>(() => GenOutputByFuelParser.Parse(Fixtures.FromString(xml)));
        Assert.Contains("RealtimeOntarioZonalPrice", ex.Message);
    }

    [Fact]
    public void Refuses_DTDs_to_block_XML_external_entity_attacks()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE Document [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <Document docID="GenOutputbyFuelHourly" xmlns="http://www.ieso.ca/schema"><DocBody>&xxe;</DocBody></Document>
            """;

        Assert.Throws<IesoFormatException>(() => GenOutputByFuelParser.Parse(Fixtures.FromString(xml)));
    }
}

public class DemandCsvParserTests
{
    [Fact]
    public void Parses_real_demand_file_and_skips_metadata_lines()
    {
        using var reader = new StreamReader(Fixtures.Open("PUB_Demand_sample.csv"));

        var result = DemandCsvParser.Parse(reader);

        Assert.Equal(14 * 24, result.Items.Count);
        Assert.Empty(result.Warnings);
        var first = result.Items[0];
        Assert.Equal(new DateTime(2026, 10, 17, 5, 0, 0, DateTimeKind.Utc), first.HourStartUtc);
        Assert.True(first.MarketDemandMw >= first.OntarioDemandMw, "Market demand includes exports, so it is at least Ontario demand.");
    }

    [Fact]
    public void Columns_map_market_then_Ontario_demand()
    {
        var csv = "\\\\Hourly Demand Report,,,\nDate,Hour,Market Demand,Ontario Demand\n2026-01-01,1,19489,16526\n";

        var item = Assert.Single(DemandCsvParser.Parse(new StringReader(csv)).Items);

        Assert.Equal(19489, item.MarketDemandMw);
        Assert.Equal(16526, item.OntarioDemandMw);
    }

    [Fact]
    public void Warns_on_malformed_rows_without_failing_the_file()
    {
        var csv = "Date,Hour,Market Demand,Ontario Demand\n2026-01-01,1,19489,16526\n2026-01-01,25,1,1\nnot,a,row\n2026-01-01,2,19317,16374\n";

        var result = DemandCsvParser.Parse(new StringReader(csv));

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Throws_when_the_header_is_missing()
    {
        Assert.Throws<IesoFormatException>(() => DemandCsvParser.Parse(new StringReader("2026-01-01,1,19489,16526\n")));
    }
}

public class RealtimeParserTests
{
    [Fact]
    public void Price_report_keeps_only_published_intervals()
    {
        var xml = Fixtures.PriceReport("2026-10-31", 12, (1, "82.47"), (2, "82.47"), (3, "76.78"));

        var items = ZonalPriceParser.Parse(Fixtures.FromString(xml)).Items;

        Assert.Equal(3, items.Count);
        Assert.Equal(new DateTime(2026, 10, 31, 16, 10, 0, DateTimeKind.Utc), items[2].IntervalStartUtc);
        Assert.Equal(76.78m, items[2].PricePerMwh);
    }

    [Fact]
    public void Negative_prices_are_valid()
    {
        var xml = Fixtures.PriceReport("2026-04-12", 3, (1, "-4.12"));

        Assert.Equal(-4.12m, Assert.Single(ZonalPriceParser.Parse(Fixtures.FromString(xml)).Items).PricePerMwh);
    }

    [Fact]
    public void Totals_report_extracts_Ontario_demand_and_ignores_other_quantities()
    {
        var xml = Fixtures.TotalsReport("2026-10-31", 12, (1, "15286.2"), (2, "15316.2"));

        var items = RealtimeTotalsParser.Parse(Fixtures.FromString(xml)).Items;

        Assert.Equal([15286.2, 15316.2], items.Select(i => i.OntarioDemandMw));
        Assert.Equal(new DateTime(2026, 10, 31, 16, 5, 0, DateTimeKind.Utc), items[1].IntervalStartUtc);
    }
}
