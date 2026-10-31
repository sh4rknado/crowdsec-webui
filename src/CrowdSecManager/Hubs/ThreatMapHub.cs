using CrowdSecManager.Models;
using Microsoft.AspNetCore.SignalR;

namespace CrowdSecManager.Hubs;

/// <summary>Pushes live threat events to all connected threat-map clients.
/// Note: this hub is invoked from the Blazor ThreatMap.razor page (server-side
/// HubConnection), which is itself behind an [Authorize] page. There is no
/// direct browser-to-hub connection. If you later expose the hub to a JS
/// SignalR client, re-add [Authorize] and forward the auth cookie.</summary>
public sealed class ThreatMapHub : Hub<IThreatMapClient>
{
    private readonly ILogger<ThreatMapHub> _log;

    public ThreatMapHub(ILogger<ThreatMapHub> log) => _log = log;

    public override Task OnConnectedAsync()
    {
        _log.LogInformation("ThreatMap client connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _log.LogInformation("ThreatMap client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Called by the back-end feed to fan out a threat event.</summary>
    public static Task BroadcastAsync(IHubContext<ThreatMapHub, IThreatMapClient> ctx,
        ThreatEvent ev, ThreatMapStats stats)
    {
        return ctx.Clients.All.ThreatSpotted(ev, stats);
    }

    /// <summary>Sends an initial snapshot when a client connects.</summary>
    public Task SendSnapshot(ThreatMapSnapshot snap)
        => Clients.Caller.Snapshot(snap);
}

/// <summary>Strongly-typed client callbacks (received by the JS UI).</summary>
public interface IThreatMapClient
{
    Task ThreatSpotted(ThreatEvent ev, ThreatMapStats stats);
    Task Snapshot(ThreatMapSnapshot snapshot);
    Task StatsUpdated(ThreatMapStats stats);
}
