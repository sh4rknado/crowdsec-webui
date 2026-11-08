using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace CrowdSecManager.ViewModels;

public sealed class ThreatMapViewModel : ViewModelBase, IAsyncDisposable, IAfterRenderViewModel, IUiDispatcherAware
{
    private readonly IJSRuntime _js;
    private readonly IGeoIpService _geo;
    private readonly IThreatMapEventBus _events;
    private readonly ILogger<ThreatMapViewModel> _log;
    private readonly ThreatMapEventReplayBuffer _replayBuffer = new();
    private readonly HashSet<long> _recentAlertIds = [];
    private ThreatMapStats _stats = new();
    private IReadOnlyDictionary<string, int> _topCountries = new Dictionary<string, int>();
    private IReadOnlyList<ThreatEvent> _recent = [];
    private bool _started;
    private bool _mapReady;
    private bool _subscribed;
    private Func<Func<Task>, Task> _dispatcher = action => action();

    public ThreatMapViewModel(IJSRuntime js, IGeoIpService geo, IThreatMapEventBus events, ILogger<ThreatMapViewModel> log)
    {
        _js = js;
        _geo = geo;
        _events = events;
        _log = log;
    }

    public Task OnAfterRenderAsync(bool firstRender) => firstRender ? StartMapAsync() : Task.CompletedTask;

    public ThreatMapStats Stats { get => _stats; private set => SetProperty(ref _stats, value); }
    public IReadOnlyDictionary<string, int> TopCountries { get => _topCountries; private set => SetProperty(ref _topCountries, value); }
    public IReadOnlyList<ThreatEvent> Recent { get => _recent; private set => SetProperty(ref _recent, value); }
    public IReadOnlyList<ThreatEvent> VisibleRecent => Recent.Take(20).ToArray();
    public int TopCountryMaximum => TopCountries.Values.DefaultIfEmpty().Max();
    public int CountryPercentage(int value) => TopCountryMaximum == 0 ? 0 : 100 * value / TopCountryMaximum;
    public static string ScenarioName(string? scenario) => scenario?.Split('/').LastOrDefault() ?? "—";

    public override Task InitializeAsync()
    {
        if (!_subscribed)
        {
            _events.ThreatSpotted += OnThreatSpotted;
            _events.StatsUpdated += OnStatsUpdated;
            _subscribed = true;
        }
        Stats = _events.GetSnapshot().Stats;
        return Task.CompletedTask;
    }

    public void SetDispatcher(Func<Func<Task>, Task> dispatcher) => _dispatcher = dispatcher;

    public async Task StartMapAsync()
    {
        if (_started) return;
        _started = true;

        await _js.InvokeVoidAsync("threatMap.init", "threatCanvas", new
        {
            home = new { lat = _geo.Home.Latitude, lon = _geo.Home.Longitude, label = _geo.Home.City },
            geoAvailable = _geo.IsDatabaseAvailable
        });

        var snapshot = _events.GetSnapshot();
        Stats = snapshot.Stats;
        var firstDrain = true;
        while (true)
        {
            var eventsToPlot = _replayBuffer.Drain(firstDrain ? snapshot.RecentEvents : null);
            firstDrain = false;
            if (eventsToPlot.Count == 0) break;
            foreach (var threat in eventsToPlot)
            {
                if (_recentAlertIds.Add(threat.AlertId)) AddRecent(threat);
                try { await _js.InvokeVoidAsync("threatMap.plotThreat", threat); }
                catch (Exception ex) { _log.LogDebug("plotThreat: {Msg}", ex.Message); }
            }
        }
        _mapReady = true;
        RaiseComputedPropertyChanged(nameof(Stats));
    }

    private void OnThreatSpotted(ThreatEvent threat, ThreatMapStats stats)
        => _ = _dispatcher(() => ProcessThreatAsync(threat, stats));

    private async Task ProcessThreatAsync(ThreatEvent threat, ThreatMapStats stats)
    {
        if (_recentAlertIds.Add(threat.AlertId))
        {
            AddRecent(threat);
            if (_mapReady) await PlotThreatAsync(threat);
            else _replayBuffer.Enqueue(threat);
        }
        Stats = stats;
    }

    private async Task PlotThreatAsync(ThreatEvent threat)
    {
        try { await _js.InvokeVoidAsync("threatMap.plotThreat", threat); }
        catch (Exception ex) { _log.LogDebug("plotThreat: {Msg}", ex.Message); }
    }

    private void AddRecent(ThreatEvent threat)
    {
        Recent = new[] { threat }.Concat(Recent).Take(50).ToArray();
        RaiseComputedPropertyChanged(nameof(VisibleRecent));
        if (threat.Source?.Country is not { } country) return;
        var updated = new Dictionary<string, int>(_topCountries)
        {
            [country] = _topCountries.GetValueOrDefault(country) + 1
        };
        if (updated.Count > 8)
            updated = updated.OrderByDescending(item => item.Value).Take(8).ToDictionary(item => item.Key, item => item.Value);
        _topCountries = updated;
        RaiseComputedPropertyChanged(nameof(TopCountries));
        RaiseComputedPropertyChanged(nameof(TopCountryMaximum));
    }

    private void OnStatsUpdated(ThreatMapStats stats)
        => _ = _dispatcher(() => { Stats = stats; return Task.CompletedTask; });

    public async ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _events.ThreatSpotted -= OnThreatSpotted;
            _events.StatsUpdated -= OnStatsUpdated;
            _subscribed = false;
        }
        try { await _js.InvokeVoidAsync("threatMap.dispose"); }
        catch { }
    }
}
