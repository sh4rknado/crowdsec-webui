using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

/// <summary>In-process fan-out for authenticated Blazor circuits. The Blazor circuit
/// carries events to the browser, so threat data does not need a public SignalR hub.</summary>
public sealed class ThreatMapEventBus : IThreatMapEventBus
{
    private readonly object _gate = new();
    private readonly Queue<ThreatEvent> _recent = new();
    private ThreatMapStats _stats = new();
    private readonly int _capacity;

    public ThreatMapEventBus(int capacity = 60)
        => _capacity = Math.Max(1, capacity);

    public event Action<ThreatEvent, ThreatMapStats>? ThreatSpotted;
    public event Action<ThreatMapStats>? StatsUpdated;

    public void PublishThreat(ThreatEvent threat, ThreatMapStats stats)
    {
        lock (_gate)
        {
            _stats = stats;
            _recent.Enqueue(threat);
            while (_recent.Count > _capacity) _recent.Dequeue();
        }

        ThreatSpotted?.Invoke(threat, stats);
    }

    public void PublishStats(ThreatMapStats stats)
    {
        lock (_gate) _stats = stats;
        StatsUpdated?.Invoke(stats);
    }

    public ThreatMapEventSnapshot GetSnapshot()
    {
        lock (_gate) return new ThreatMapEventSnapshot(_recent.ToArray(), _stats);
    }
}
