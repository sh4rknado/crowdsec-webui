using CrowdSecManager.Models;

namespace CrowdSecManager.Services;

public sealed record ThreatMapEventSnapshot(
    IReadOnlyList<ThreatEvent> RecentEvents,
    ThreatMapStats Stats);
