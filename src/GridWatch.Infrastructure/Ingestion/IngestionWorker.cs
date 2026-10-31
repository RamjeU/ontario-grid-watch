using GridWatch.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridWatch.Infrastructure.Ingestion;

/// <summary>Tells connected clients that new data has landed. Implemented with SignalR in the API.</summary>
public interface IGridNotifier
{
    Task GridUpdatedAsync(CancellationToken ct);
}

/// <summary>
/// Polls the IESO on a schedule that matches how often each report changes: 5-minute prices and
/// demand every tick (5 minutes by default), the hourly demand report every 15 minutes, and fuel
/// output hourly. Each tick runs in its own DI scope so the DbContext is short-lived, and a failed
/// tick is logged and retried on the next one.
/// </summary>
public sealed class IngestionWorker(
    IServiceScopeFactory scopes,
    IOptions<IngestionOptions> options,
    TimeProvider clock,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly IngestionOptions _options = options.Value;
    private DateTimeOffset _lastDemand = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFuel = DateTimeOffset.MinValue;
    private bool _firstTick = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.PollMinutes), clock);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ingestion tick failed; will retry on the next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGridNotifier>();
        var now = clock.GetUtcNow();
        var changed = 0;

        var realtime = await ingestion.IngestRealtimeAsync(_firstTick ? _options.RealtimeBackfillHours : 2, ct);
        changed += Changes(realtime);

        if (now - _lastDemand >= TimeSpan.FromMinutes(_options.DemandEveryMinutes))
        {
            var demand = await ingestion.IngestDemandAsync(ct);
            changed += Changes(demand);
            if (demand.Status == IngestionStatus.Succeeded)
            {
                _lastDemand = now;
            }
        }

        if (now - _lastFuel >= TimeSpan.FromMinutes(_options.FuelOutputEveryMinutes))
        {
            var fuel = await ingestion.IngestFuelOutputAsync(ct);
            changed += Changes(fuel);
            if (fuel.Status == IngestionStatus.Succeeded)
            {
                _lastFuel = now;
            }
        }

        _firstTick = false;
        if (changed > 0)
        {
            await notifier.GridUpdatedAsync(ct);
        }
    }

    private static int Changes(IngestionSummary s) => s.Inserted + s.Updated;
}
