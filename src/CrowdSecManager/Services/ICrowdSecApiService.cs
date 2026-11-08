using System.Net.Http.Json;
using System.Text.Json;
using CrowdsecManagerModel;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>Thin HTTP client for the CrowdSec Local API (LAPI) on port 8080.</summary>
public interface ICrowdSecApiService
{
    Task<IReadOnlyList<CsAlert>> GetAlertsAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<CsDecision>> GetDecisionsAsync(CancellationToken ct = default);
    Task<ThreatMapStats> GetSnapshotStatsAsync(CancellationToken ct = default);
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}
