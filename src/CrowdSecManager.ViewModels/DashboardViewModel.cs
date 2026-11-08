using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class DashboardViewModel(ICrowdSecApiService api, ICrowdSecCliService cli, IConfigFileService config, IGeoIpService geo, ILogger<DashboardViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecApiService _api = api;
    private readonly ICrowdSecCliService _cli = cli;
    private readonly IConfigFileService _config = config;
    private readonly IGeoIpService _geo = geo;
    private readonly ILogger<DashboardViewModel> _log = log;

    public bool GeoOk { get; private set => SetProperty(ref field, value); } = true;
    public bool IsHubBusy { get; private set => SetProperty(ref field, value); }
    public bool IsConfigBusy { get; private set => SetProperty(ref field, value); }
    public string? ActionMessage { get; private set => SetProperty(ref field, value); }
    public bool ActionError { get; private set => SetProperty(ref field, value); }
    public bool GeoDatabaseAvailable => _geo.IsDatabaseAvailable;

    public ThreatMapStats Stats { get; private set => SetProperty(ref field, value); } = new();
    public IReadOnlyList<CsDecision> RecentDecisions { get; private set => SetProperty(ref field, value); } = [];

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
