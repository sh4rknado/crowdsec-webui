using System.Net.Http.Json;
using System.Text.Json;
using CrowdsecManagerModel;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

public sealed class CrowdSecApiService : ICrowdSecApiService
{
    private readonly HttpClient _http;
    private readonly CrowdSecOptions _opt;
    private readonly ILogger<CrowdSecApiService> _log;
    private readonly ICrowdSecCliService _cli;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CrowdSecApiService(HttpClient http, IOptions<CrowdSecOptions> opt,
        ILogger<CrowdSecApiService> log, ICrowdSecCliService cli)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = (opt ?? throw new ArgumentNullException(nameof(opt))).Value;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        var baseAddr = _opt.LocalApiUrl.TrimEnd('/');
        if (!baseAddr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseAddr = "http://" + baseAddr;
        _http.BaseAddress = new Uri(baseAddr + "/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(_opt.BouncerApiKey))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _opt.BouncerApiKey);
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"crowdsec-manager/1.0");
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("health", ct);
            return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogDebug("LAPI unreachable: {Msg}", ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<CsAlert>> GetAlertsAsync(int limit = 100, CancellationToken ct = default)
    {
        // Bouncer tokens cannot access the alert search endpoint. Use cscli, which
        // authenticates as the CrowdSec machine and can read the full alert history.
        return await _cli.ListAlertsAsync(limit, ct);
    }

    public async Task<IReadOnlyList<CsDecision>> GetDecisionsAsync(CancellationToken ct = default)
    {
        var list = await GetJsonAsync<List<CsDecision>>("v1/decisions", ct);
        return list ?? await _cli.ListDecisionsAsync(ct);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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
