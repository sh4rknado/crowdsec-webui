using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

public sealed class ThreatMapEventReplayBuffer
{
    private readonly object _gate = new();
    private readonly Queue<ThreatEvent> _pending = new();
    private readonly int _capacity;

    public ThreatMapEventReplayBuffer(int capacity = 60) => _capacity = Math.Max(1, capacity);

    public void Enqueue(ThreatEvent threat)
    {
        lock (_gate)
        {
            while (_pending.Count >= _capacity) _pending.Dequeue();
            _pending.Enqueue(threat);
        }
    }

    public IReadOnlyList<ThreatEvent> Drain(IEnumerable<ThreatEvent>? snapshot = null)
    {
        lock (_gate)
        {
            var unique = new Dictionary<long, ThreatEvent>();
            if (snapshot is not null)
                foreach (var threat in snapshot)
                    unique.TryAdd(threat.AlertId, threat);
            while (_pending.TryDequeue(out var threat))
                unique.TryAdd(threat.AlertId, threat);
            return unique.Values.OrderBy(threat => threat.CreatedAt).ThenBy(threat => threat.AlertId).ToArray();
        }
    }
}
