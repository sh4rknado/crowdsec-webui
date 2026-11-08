using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CrowdSecManager.ViewModels;

public sealed class AlertsViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<AlertsViewModel> _log;
    private int _limit = 100;
    private string _ip = "";
    private string _scenario = "";
    private string _since = "";
    private string _until = "";
    private string _type = "";
    private IReadOnlyList<CsAlert> _alerts = [];

    public AlertsViewModel(ICrowdSecCliService cli, ILogger<AlertsViewModel> log) { _cli = cli; _log = log; }

    public int Limit { get => _limit; set => SetProperty(ref _limit, value); }
    public string Ip { get => _ip; set => SetProperty(ref _ip, value); }
    public string Scenario { get => _scenario; set => SetProperty(ref _scenario, value); }
    public string Since { get => _since; set => SetProperty(ref _since, value); }
    public string Until { get => _until; set => SetProperty(ref _until, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public IReadOnlyList<CsAlert> Alerts { get => _alerts; private set => SetProperty(ref _alerts, value); }

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            Alerts = await _cli.ListAlertsAsync(Math.Clamp(Limit, 1, 1000), NullIfBlank(Ip), NullIfBlank(Scenario), NullIfBlank(Since), NullIfBlank(Until), NullIfBlank(Type));
        }
        catch (Exception ex) { _log.LogDebug("alerts: {Msg}", ex.Message); }
    }

    public async Task ClearFiltersAsync()
    {
        Ip = Scenario = Since = Until = Type = "";
        await RefreshAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var result = await _cli.RunAsync("alerts", "delete", "--id", id.ToString(CultureInfo.InvariantCulture));
        if (!result.Success) _log.LogWarning("Could not delete alert {Id}: {Error}", id, result.FlattenError());
        await RefreshAsync();
    }

    public static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) ? value : value.Length > length ? value[..length] + "..." : value;

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
