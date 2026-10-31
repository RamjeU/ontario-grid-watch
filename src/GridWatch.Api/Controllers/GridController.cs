using GridWatch.Api.Security;
using GridWatch.Core.Analytics;
using GridWatch.Infrastructure.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GridWatch.Api.Controllers;

/// <summary>Read-only access to Ontario grid data. All times are UTC (ISO 8601).</summary>
[ApiController]
[Route("api/grid")]
[EnableRateLimiting(RateLimits.Read)]
public sealed class GridController(GridQueryService queries, TimeProvider clock) : ControllerBase
{
    /// <summary>Latest demand, price and generation mix, each with its own timestamp.</summary>
    [HttpGet("snapshot")]
    [ProducesResponseType<GridSnapshot>(StatusCodes.Status200OK)]
    public async Task<GridSnapshot> GetSnapshot(CancellationToken ct) => await queries.GetSnapshotAsync(ct);

    /// <summary>5-minute Ontario demand (MW) and Ontario Zonal Price ($/MWh). Defaults to the last 24 hours; at most 7 days.</summary>
    [HttpGet("realtime")]
    [ProducesResponseType<IReadOnlyList<RealtimePoint>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRealtime(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        if (!TryRange(from, to, TimeSpan.FromDays(1), out var range, out var problem, maxSpan: TimeSpan.FromDays(7)))
        {
            return problem;
        }

        return Ok(await queries.GetRealtimeAsync(range, ct));
    }

    /// <summary>Generator output by fuel type, hourly or as daily averages. Defaults to the last 7 days.</summary>
    [HttpGet("generation")]
    [ProducesResponseType<IReadOnlyList<GenerationPoint>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetGeneration(DateTimeOffset? from, DateTimeOffset? to, Resolution resolution = Resolution.Hour, CancellationToken ct = default)
    {
        if (!TryRange(from, to, TimeSpan.FromDays(7), out var range, out var problem))
        {
            return problem;
        }

        return Ok(await queries.GetGenerationAsync(range, resolution, ct));
    }

    /// <summary>Hourly Ontario and market demand in MW. Defaults to the last 7 days.</summary>
    [HttpGet("demand")]
    [ProducesResponseType<IReadOnlyList<DemandPoint>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDemand(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        if (!TryRange(from, to, TimeSpan.FromDays(7), out var range, out var problem))
        {
            return problem;
        }

        return Ok(await queries.GetDemandAsync(range, ct));
    }

    /// <summary>Real-time Ontario Zonal Price per hour ($/MWh): average, minimum and maximum of the 5-minute intervals.</summary>
    [HttpGet("prices")]
    [ProducesResponseType<IReadOnlyList<HourlyPricePoint>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPrices(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        if (!TryRange(from, to, TimeSpan.FromDays(7), out var range, out var problem))
        {
            return problem;
        }

        return Ok(await queries.GetHourlyPricesAsync(range, ct));
    }

    /// <summary>One row per market day: peak and average demand, average price, clean share and estimated carbon intensity.</summary>
    [HttpGet("daily")]
    [ProducesResponseType<IReadOnlyList<DailySummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDaily(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        if (!TryRange(from, to, TimeSpan.FromDays(14), out var range, out var problem))
        {
            return problem;
        }

        return Ok(await queries.GetDailySummaryAsync(range, ct));
    }

    private bool TryRange(DateTimeOffset? from, DateTimeOffset? to, TimeSpan defaultSpan, out TimeRange range, out IActionResult problem, TimeSpan? maxSpan = null)
    {
        if (TimeRange.TryCreate(from, to, defaultSpan, clock.GetUtcNow().UtcDateTime, out range, out var error, maxSpan))
        {
            problem = null!;
            return true;
        }

        problem = Problem(title: "Invalid time range", detail: error, statusCode: StatusCodes.Status400BadRequest);
        return false;
    }
}
