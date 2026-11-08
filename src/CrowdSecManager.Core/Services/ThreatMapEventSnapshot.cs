using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

public sealed record ThreatMapEventSnapshot
{
    public ThreatMapEventSnapshot(IReadOnlyList<ThreatEvent> RecentEvents, ThreatMapStats Stats)
    {
        this.RecentEvents = RecentEvents ?? throw new ArgumentNullException(nameof(RecentEvents));
        this.Stats = Stats ?? throw new ArgumentNullException(nameof(Stats));
    }

    public IReadOnlyList<ThreatEvent> RecentEvents { get; init; }
    public ThreatMapStats Stats { get; init; }

    public void Deconstruct(out IReadOnlyList<ThreatEvent> RecentEvents, out ThreatMapStats Stats)
    {
        RecentEvents = this.RecentEvents;
        Stats = this.Stats;
    }
}
