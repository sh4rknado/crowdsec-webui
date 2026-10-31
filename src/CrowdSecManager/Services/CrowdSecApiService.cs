using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CrowdSecManager.Models;
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

public sealed class CrowdSecApiService : ICrowdSecApiService
{
    private readonly HttpClient _http;
    private readonly CrowdSecOptions _opt;
    private readonly ILogger<CrowdSecApiService> _log;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CrowdSecApiService(HttpClient http, IOptions<CrowdSecOptions> opt,
        ILogger<CrowdSecApiService> log)
    {
        _opt = opt.Value;
        _log = log;
        var baseAddr = _opt.LocalApiUrl.TrimEnd('/');
        if (!baseAddr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseAddr = "http://" + baseAddr;
        http.BaseAddress = new Uri(baseAddr + "/");
        http.Timeout = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(_opt.BouncerApiKey))
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opt.BouncerApiKey);
        }
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"crowdsec-manager/1.0");
        _http = http;
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("health", ct);
            return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception ex)
        {
            _log.LogDebug("LAPI unreachable: {Msg}", ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<CsAlert>> GetAlertsAsync(int limit = 100, CancellationToken ct = default)
    {
        var list = await GetJsonAsync<List<CsAlert>>($"alerts?limit={limit}", ct);
        return list ?? new List<CsAlert>();
    }

    public async Task<IReadOnlyList<CsDecision>> GetDecisionsAsync(CancellationToken ct = default)
    {
        var list = await GetJsonAsync<List<CsDecision>>("decisions", ct);
        return list ?? new List<CsDecision>();
    }

    public async Task<ThreatMapStats> GetSnapshotStatsAsync(CancellationToken ct = default)
    {
        var stats = new ThreatMapStats();
        try
        {
            var decisions = await GetDecisionsAsync(ct);
            stats.TotalDecisions = decisions.Count;

            var alerts = await GetAlertsAsync(500, ct);
            stats.TotalAlerts = alerts.Count;
            stats.Last24hAlerts = alerts.Count(a => a.CreatedAt > DateTime.UtcNow.AddDays(-1));

            stats.TopScenarios = alerts
                .GroupBy(a => a.Scenario ?? "unknown")
                .OrderByDescending(g => g.Count())
                .Take(8)
                .ToDictionary(g => g.Key!, g => g.Count());
        }
        catch (Exception ex)
        {
            _log.LogDebug("Failed to compute snapshot stats: {Msg}", ex.Message);
        }
        return stats;
    }

    private async Task<T?> GetJsonAsync<T>(string relative, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<T>(relative, Json, ct);
        }
        catch (HttpRequestException ex)
        {
            _log.LogDebug("LAPI GET {Path} failed: {Status}", relative, ex.Message);
            return default;
        }
        catch (JsonException ex)
        {
            _log.LogDebug("LAPI GET {Path} JSON parse error: {Msg}", relative, ex.Message);
            return default;
        }
    }
}
