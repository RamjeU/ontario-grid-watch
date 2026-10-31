using System.Net;
using GridWatch.Core.Domain;
using GridWatch.Core.Parsing;

namespace GridWatch.Infrastructure.Ieso;

/// <summary>
/// Typed HTTP client for the IESO public reports site (https://reports-public.ieso.ca/public/).
/// Retries, timeouts and a circuit breaker come from the standard resilience handler registered in DI.
/// </summary>
public sealed class IesoReportClient(HttpClient http)
{
    public async Task<ParseResult<FuelOutputReading>> GetFuelOutputAsync(int year, CancellationToken ct)
    {
        await using var stream = await GetStreamAsync($"GenOutputbyFuelHourly/PUB_GenOutputbyFuelHourly_{year}.xml", ct)
            ?? throw new IesoReportNotFoundException($"No fuel output report published for {year}.");
        return GenOutputByFuelParser.Parse(stream);
    }

    public async Task<ParseResult<DemandReading>> GetDemandAsync(int year, CancellationToken ct)
    {
        await using var stream = await GetStreamAsync($"Demand/PUB_Demand_{year}.csv", ct)
            ?? throw new IesoReportNotFoundException($"No demand report published for {year}.");
        using var reader = new StreamReader(stream);
        return DemandCsvParser.Parse(reader);
    }

    /// <summary>Returns null when the hour's file has not been published yet.</summary>
    public async Task<ParseResult<PriceReading>?> GetZonalPricesAsync(DateOnly date, int hourEnding, CancellationToken ct)
    {
        await using var stream = await GetStreamAsync($"RealtimeOntarioZonalPrice/PUB_RealtimeOntarioZonalPrice_{date:yyyyMMdd}{hourEnding:00}.xml", ct);
        return stream is null ? null : ZonalPriceParser.Parse(stream);
    }

    /// <summary>Returns null when the hour's file has not been published yet.</summary>
    public async Task<ParseResult<LiveDemandReading>?> GetRealtimeTotalsAsync(DateOnly date, int hourEnding, CancellationToken ct)
    {
        await using var stream = await GetStreamAsync($"RealtimeTotals/PUB_RealtimeTotals_{date:yyyyMMdd}{hourEnding:00}.xml", ct);
        return stream is null ? null : RealtimeTotalsParser.Parse(stream);
    }

    private async Task<Stream?> GetStreamAsync(string path, CancellationToken ct)
    {
        var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        response.EnsureSuccessStatusCode();

        // Buffer so the response can be disposed independently of the parser.
        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        response.Dispose();
        buffer.Position = 0;
        return buffer;
    }
}

public sealed class IesoReportNotFoundException(string message) : Exception(message);
