using GridWatch.Api.Security;
using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ingestion;
using GridWatch.Infrastructure.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GridWatch.Api.Controllers;

public enum IngestTarget
{
    All,
    Realtime,
    Demand,
    FuelOutput,
}

/// <summary>Ingestion status, and a protected endpoint to trigger a run on demand.</summary>
[ApiController]
[Route("api/ingestion")]
public sealed class IngestionController(GridQueryService queries) : ControllerBase
{
    /// <summary>The most recent ingestion attempts, newest first, including failures and their errors.</summary>
    [HttpGet("runs")]
    [EnableRateLimiting(RateLimits.Read)]
    [ProducesResponseType<IReadOnlyList<IngestionRun>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<IngestionRun>> GetRuns(CancellationToken ct, int count = 20) =>
        await queries.GetRecentRunsAsync(Math.Clamp(count, 1, 100), ct);

    /// <summary>Runs ingestion now. Requires the X-Api-Key header.</summary>
    [HttpPost("run")]
    [EnableRateLimiting(RateLimits.Admin)]
    [ServiceFilter<ApiKeyFilter>]
    [ProducesResponseType<IReadOnlyList<IngestionSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IReadOnlyList<IngestionSummary>> Run(
        [FromServices] IngestionService ingestion,
        [FromServices] IGridNotifier notifier,
        CancellationToken ct,
        IngestTarget target = IngestTarget.All)
    {
        var results = new List<IngestionSummary>();
        if (target is IngestTarget.All or IngestTarget.Realtime)
        {
            results.Add(await ingestion.IngestRealtimeAsync(hours: 3, ct));
        }

        if (target is IngestTarget.All or IngestTarget.Demand)
        {
            results.Add(await ingestion.IngestDemandAsync(ct));
        }

        if (target is IngestTarget.All or IngestTarget.FuelOutput)
        {
            results.Add(await ingestion.IngestFuelOutputAsync(ct));
        }

        if (results.Any(r => r.Inserted + r.Updated > 0))
        {
            await notifier.GridUpdatedAsync(ct);
        }

        return results;
    }
}
