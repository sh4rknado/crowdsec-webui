using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CrowdSecManager.ViewModels;

public sealed class AlertsViewModel(ICrowdSecCliService cli, ILogger<AlertsViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli;
    private readonly ILogger<AlertsViewModel> _log = log;

    public int Limit { get; set => SetProperty(ref field, value); } = 100;
    public string Ip { get; set => SetProperty(ref field, value); } = "";
    public string Scenario { get; set => SetProperty(ref field, value); } = "";
    public string Since { get; set => SetProperty(ref field, value); } = "";
    public string Until { get; set => SetProperty(ref field, value); } = "";
    public string Type { get; set => SetProperty(ref field, value); } = "";

    public IReadOnlyList<CsAlert> Alerts { get; private set => SetProperty(ref field, value); } = [];

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
