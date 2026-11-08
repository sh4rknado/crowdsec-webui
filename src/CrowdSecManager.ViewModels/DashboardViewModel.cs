using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ICrowdSecApiService _api;
    private readonly ICrowdSecCliService _cli;
    private readonly IConfigFileService _config;
    private readonly IGeoIpService _geo;
    private readonly ILogger<DashboardViewModel> _log;
    private ThreatMapStats _stats = new();
    private IReadOnlyList<CsDecision> _recentDecisions = [];
    private bool _geoOk = true;
    private bool _isHubBusy;
    private bool _isConfigBusy;
    private string? _actionMessage;
    private bool _actionError;

    public DashboardViewModel(ICrowdSecApiService api, ICrowdSecCliService cli, IConfigFileService config, IGeoIpService geo, ILogger<DashboardViewModel> log)
    {
        _api = api;
        _cli = cli;
        _config = config;
        _geo = geo;
        _log = log;
    }

    public ThreatMapStats Stats { get => _stats; private set => SetProperty(ref _stats, value); }
    public IReadOnlyList<CsDecision> RecentDecisions { get => _recentDecisions; private set => SetProperty(ref _recentDecisions, value); }
    public bool GeoOk { get => _geoOk; private set => SetProperty(ref _geoOk, value); }
    public bool IsHubBusy { get => _isHubBusy; private set => SetProperty(ref _isHubBusy, value); }
    public bool IsConfigBusy { get => _isConfigBusy; private set => SetProperty(ref _isConfigBusy, value); }
    public string? ActionMessage { get => _actionMessage; private set => SetProperty(ref _actionMessage, value); }
    public bool ActionError { get => _actionError; private set => SetProperty(ref _actionError, value); }
    public bool GeoDatabaseAvailable => _geo.IsDatabaseAvailable;

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            Stats = await _api.GetSnapshotStatsAsync();
            RecentDecisions = await _api.GetDecisionsAsync();
            GeoOk = _geo.IsDatabaseAvailable;
        }
        catch (Exception ex) { _log.LogDebug("refresh: {Msg}", ex.Message); }
    }

    public async Task HubUpdateAsync()
    {
        IsHubBusy = true;
        ActionMessage = null;
        try
        {
            var result = await _cli.HubUpdateAsync();
            ActionError = !result.Success;
            ActionMessage = result.Success
                ? $"Hub index updated in {result.Duration.TotalSeconds:F1}s."
                : $"cscli hub update failed: {result.FlattenError() ?? result.Stderr}";
        }
        finally { IsHubBusy = false; }
    }

    public async Task HubUpgradeAsync()
    {
        IsHubBusy = true;
        ActionMessage = null;
        try
        {
            var result = await _cli.HubUpgradeAsync();
            ActionError = !result.Success;
            ActionMessage = result.Success
                ? $"All hub items upgraded in {result.Duration.TotalSeconds:F1}s."
                : $"cscli hub upgrade failed: {result.FlattenError() ?? result.Stderr}";
        }
        finally { IsHubBusy = false; }
    }

    public async Task BackupConfigAsync()
    {
        IsConfigBusy = true;
        ActionMessage = null;
        try
        {
            const string directory = "/var/lib/crowdsec-manager/backups";
            await _config.BackupAsync(directory);
            ActionError = false;
            ActionMessage = $"Configuration backed up to {directory}";
        }
        catch (Exception ex) { ActionError = true; ActionMessage = ex.Message; }
        finally { IsConfigBusy = false; }
    }

    public async Task DeleteDecisionAsync(long id)
    {
        var result = await _cli.DeleteDecisionAsync(id: id);
        if (result.Success) await RefreshAsync();
        else { ActionError = true; ActionMessage = $"Delete failed: {result.FlattenError()}"; }
    }
}
