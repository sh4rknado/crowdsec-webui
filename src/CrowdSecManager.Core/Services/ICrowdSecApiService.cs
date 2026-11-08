using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

public interface ICrowdSecApiService
{
    Task<IReadOnlyList<CsAlert>> GetAlertsAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<CsDecision>> GetDecisionsAsync(CancellationToken ct = default);
    Task<ThreatMapStats> GetSnapshotStatsAsync(CancellationToken ct = default);
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}
