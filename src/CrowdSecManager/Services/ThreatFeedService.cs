using System.Collections.Concurrent;
using CrowdSecManager.Data;
using CrowdsecManagerModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>Background service that polls CrowdSec (alerts/decisions via LAPI),
/// geolocates new attacker IPs and publishes events to the threat-map event bus.</summary>
public sealed class ThreatFeedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IThreatMapEventBus _events;
    private readonly IGeoIpService _geo;
    private readonly ThreatFeedOptions _opt;
    private readonly GeoIpOptions _geoOpt;
    private readonly ILogger<ThreatFeedService> _log;

    private readonly ConcurrentDictionary<string, ThreatEvent> _recent = new();
    private readonly ConcurrentDictionary<long, byte> _seenAlerts = new();
    private readonly ConcurrentDictionary<long, ThreatHistoryEntry> _pendingHistory = new();
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _seenAlertInitialization = new(1, 1);
    private bool _seenAlertsInitialized;

    public ThreatFeedService(IServiceProvider sp,
        IThreatMapEventBus events,
        IGeoIpService geo,
        IOptions<ThreatFeedOptions> opt,
        IOptions<GeoIpOptions> geoOpt,
        ILogger<ThreatFeedService> log)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _geo = geo ?? throw new ArgumentNullException(nameof(geo));
        _opt = (opt ?? throw new ArgumentNullException(nameof(opt))).Value;
        _geoOpt = (geoOpt ?? throw new ArgumentNullException(nameof(geoOpt))).Value;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("ThreatFeedService starting (poll every {S}s)", _opt.PollIntervalSeconds);

        // Allow CrowdSec to come up first
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Max(3, _opt.PollIntervalSeconds)));

        do
        {
            try { await PollOnceAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogDebug("ThreatFeed poll error: {Msg}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(ct));

        _log.LogInformation("ThreatFeedService stopped");
    }

    internal async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<ICrowdSecApiService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await api.IsReachableAsync(ct))
        {
            // Fall back to CLI
            await PollViaCliAsync(scope, db, ct);
            return;
        }

        // The manager authenticates to LAPI as a bouncer, which is intentionally
        // limited to decisions. cscli uses machine credentials for alert history.
        var alerts = await scope.ServiceProvider.GetRequiredService<ICrowdSecCliService>()
            .ListAlertsAsync(100, ct);
        var decisions = await api.GetDecisionsAsync(ct);
        await ProcessBatchAsync(alerts, decisions, db, ct);
    }

    private async Task PollViaCliAsync(IServiceScope scope, AppDbContext db, CancellationToken ct)
    {
        var cli = scope.ServiceProvider.GetRequiredService<ICrowdSecCliService>();
        var alerts = await cli.ListAlertsAsync(100, ct);
        var decisions = await cli.ListDecisionsAsync(ct);
        await ProcessBatchAsync(alerts, decisions, db, ct);
    }

    internal async Task ProcessBatchAsync(IReadOnlyList<CsAlert> alerts,
        IReadOnlyList<CsDecision> decisions, AppDbContext db, CancellationToken ct = default)
    {
        await InitializeSeenAlertsAsync(db, ct);
        await RetryPendingHistoryAsync(db, ct);
        var stats = BuildStats(alerts, decisions);

        var fresh = alerts
            .OrderBy(a => a.CreatedAt)
            .Where(a => !_seenAlerts.ContainsKey(a.Id))
            .Take(20)
            .Where(a => _seenAlerts.TryAdd(a.Id, 0))
            .ToList();

        foreach (var a in fresh)
        {
            var ev = ToThreatEvent(a);
            if (ev is null) continue;
            if (!await PersistHistoryAsync(db, ev, ct))
                _pendingHistory[ev.AlertId] = ToHistoryEntry(ev);

            while (_recent.Count >= Math.Max(1, _opt.MaxActiveArcs))
            {
                var oldest = _recent.OrderBy(x => x.Value.CreatedAt).FirstOrDefault();
                if (oldest.Key is null || !_recent.TryRemove(oldest.Key, out _)) break;
            }
            _recent[ev.Id] = ev;
            _events.PublishThreat(ev, stats);
        }

        _events.PublishStats(stats);
    }

    private async Task RetryPendingHistoryAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var entry in _pendingHistory.Values)
        {
            if (await PersistHistoryEntryAsync(db, entry, ct))
                _pendingHistory.TryRemove(entry.AlertId, out _);
        }
    }

    private async Task InitializeSeenAlertsAsync(AppDbContext db, CancellationToken ct)
    {
        if (_seenAlertsInitialized) return;

        await _seenAlertInitialization.WaitAsync(ct);
        try
        {
            if (_seenAlertsInitialized) return;
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var persistedIds = await db.ThreatHistory.AsNoTracking()
                .Where(entry => entry.CreatedAt >= cutoff)
                .Select(entry => entry.AlertId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in persistedIds) _seenAlerts.TryAdd(id, 0);
            _seenAlertsInitialized = true;
        }
        finally
        {
            _seenAlertInitialization.Release();
        }
    }

    private static ThreatMapStats BuildStats(IReadOnlyList<CsAlert> alerts, IReadOnlyList<CsDecision> decisions)
        => new()
        {
            TotalAlerts = alerts.Count,
            TotalDecisions = decisions.Count,
            Last24hAlerts = alerts.Count(a => a.CreatedAt > DateTime.UtcNow.AddDays(-1)),
            TopScenarios = alerts.GroupBy(a => a.Scenario ?? "unknown")
                .OrderByDescending(g => g.Count()).Take(8)
                .ToDictionary(g => g.Key!, g => g.Count())
        };

    private ThreatEvent? ToThreatEvent(CsAlert a)
    {
        var ip = ExtractSourceIp(a);
        if (string.IsNullOrWhiteSpace(ip)) return null;

        var src = _geo.Resolve(ip);
        var ev = new ThreatEvent
        {
            AlertId = a.Id,
            SourceIp = ip,
            Source = src,
            Destination = _geo.Home,
            Scenario = a.Scenario,
            Message = a.Message,
            CreatedAt = a.CreatedAt == default ? DateTime.UtcNow : a.CreatedAt,
            Severity = Classify(a)
        };
        return ev;
    }

    private static string? ExtractSourceIp(CsAlert a)
    {
        // cscli/LAPI expose source either as the alert.Source string or in Meta / Events
        if (!string.IsNullOrWhiteSpace(a.Source) && IPAddressString(a.Source!))
            return a.Source!.Split('/')[0];

        if (a.Meta is { Count: > 0 })
        {
            var src = a.Meta.FirstOrDefault(m =>
                string.Equals(m.Key, "source_ip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.Key, "src_ip", StringComparison.OrdinalIgnoreCase));
            if (src is not null && !string.IsNullOrWhiteSpace(src.Value))
                return src.Value!.Split('/')[0];
        }

        if (a.Events is { Count: > 0 })
        {
            foreach (var e in a.Events)
            {
                if (e.Meta is null) continue;
                if (e.Meta.TryGetValue("source_ip", out var v1) && v1 is string s1 && IPAddressString(s1))
                    return s1.Split('/')[0];
                if (e.Meta.TryGetValue("src_ip", out var v2) && v2 is string s2 && IPAddressString(s2))
                    return s2.Split('/')[0];
            }
        }
        return null;
    }

    private static bool IPAddressString(string s) => System.Net.IPAddress.TryParse(s.Split('/')[0], out _);

    private static ThreatSeverity Classify(CsAlert a)
    {
        var sc = (a.Scenario ?? "").ToLowerInvariant();
        if (sc.Contains("sqli") || sc.Contains("rce") || sc.Contains("ssh-bf")) return ThreatSeverity.High;
        if (sc.Contains("bf") || sc.Contains("brute")) return ThreatSeverity.Medium;
        if (sc.Contains("scan") || sc.Contains("crawl")) return ThreatSeverity.Low;
        return ThreatSeverity.Info;
    }

    private Task<bool> PersistHistoryAsync(AppDbContext db, ThreatEvent ev, CancellationToken ct)
        => PersistHistoryEntryAsync(db, ToHistoryEntry(ev), ct);

    private static ThreatHistoryEntry ToHistoryEntry(ThreatEvent ev)
        => new()
        {
            AlertId = ev.AlertId,
            CreatedAt = ev.CreatedAt,
            SourceIp = ev.SourceIp,
            CountryIso = ev.Source?.CountryIsoCode,
            Country = ev.Source?.Country,
            City = ev.Source?.City,
            Latitude = ev.Source?.Latitude,
            Longitude = ev.Source?.Longitude,
            Scenario = ev.Scenario,
            Message = ev.Message,
            Severity = ev.Severity.ToString()
        };

    private async Task<bool> PersistHistoryEntryAsync(AppDbContext db, ThreatHistoryEntry entry, CancellationToken ct)
    {
        try
        {
            lock (_historyLock) db.ThreatHistory.Add(entry);

            // Trim old entries (>30 days) periodically
            if (db.ThreatHistory.Count() % 100 == 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-30);
                await db.ThreatHistory.Where(h => h.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
            }
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (db.Entry(entry).State != EntityState.Detached)
                db.Entry(entry).State = EntityState.Detached;
            _log.LogWarning(ex, "Could not persist threat history for alert {AlertId}; will retry", entry.AlertId);
            return false;
        }
    }
}
