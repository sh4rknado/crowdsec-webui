using CrowdSecManager.Models;

namespace CrowdSecManager.Services;

public interface IThreatMapEventBus
{
    event Action<ThreatEvent, ThreatMapStats>? ThreatSpotted;
    event Action<ThreatMapStats>? StatsUpdated;

    void PublishThreat(ThreatEvent threat, ThreatMapStats stats);
    void PublishStats(ThreatMapStats stats);
    ThreatMapEventSnapshot GetSnapshot();
}
