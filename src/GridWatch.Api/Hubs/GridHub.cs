using GridWatch.Infrastructure.Ingestion;
using Microsoft.AspNetCore.SignalR;

namespace GridWatch.Api.Hubs;

/// <summary>
/// Real-time channel. The server pushes a "gridUpdated" message after each ingestion that changed
/// data, and clients refetch what they display. Pushing a signal rather than the data keeps the hub
/// independent of whichever range each client is viewing.
/// </summary>
public sealed class GridHub : Hub
{
    public const string GridUpdated = "gridUpdated";
}

public sealed class SignalRGridNotifier(IHubContext<GridHub> hub) : IGridNotifier
{
    public Task GridUpdatedAsync(CancellationToken ct) => hub.Clients.All.SendAsync(GridHub.GridUpdated, ct);
}
