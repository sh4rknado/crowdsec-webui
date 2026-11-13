using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace CrowdSecManager.ViewModels;

public sealed class AlertsViewModel(ICrowdSecCliService cli, ILogger<AlertsViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<AlertsViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public int Limit { get; set => SetProperty(ref field, value); } = 100;
    public string Ip { get; set => SetProperty(ref field, value); } = "";
    public string Scenario { get; set => SetProperty(ref field, value); } = "";
    public string Since { get; set => SetProperty(ref field, value); } = "";
    public string Until { get; set => SetProperty(ref field, value); } = "";
    public string Type { get; set => SetProperty(ref field, value); } = "";

    public IReadOnlyList<CsAlert> Alerts { get; private set => SetProperty(ref field, value); } = [];
    public bool IsBanning { get; private set => SetProperty(ref field, value); }
    public string? ActionMessage { get; private set => SetProperty(ref field, value); }
    public bool ActionHasError { get; private set => SetProperty(ref field, value); }

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

    public bool CanBan(CsAlert alert)
    {
        var scope = alert.Source?.Scope;
        return (string.Equals(scope, "Ip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "Range", StringComparison.OrdinalIgnoreCase))
            && IsValidIpOrRange(alert.Source?.Value);
    }

    public async Task BanAsync(CsAlert alert)
    {
        if (!CanBan(alert)) return;

        var target = alert.Source!.Value!;
        IsBanning = true;
        ActionMessage = null;
        ActionHasError = false;
        try
        {
            var reason = string.IsNullOrWhiteSpace(alert.Scenario) ? "Alert" : alert.Scenario;
            var result = await _cli.AddDecisionAsync(target, "4h", reason, "ban");
            ActionHasError = !result.Success;
            ActionMessage = result.Success
                ? $"Ban added for {target} for 4h."
                : $"Could not ban {target}: {result.FlattenError() ?? result.Stderr}";
            if (result.Success) await RefreshAsync();
        }
        catch (Exception ex)
        {
            ActionHasError = true;
            ActionMessage = $"Could not ban {target}: {ex.Message}";
            _log.LogWarning(ex, "Could not ban alert source {Target}", target);
        }
        finally { IsBanning = false; }
    }

    public static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) ? value : value.Length > length ? value[..length] + "..." : value;

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidIpOrRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var target = value.Trim();
        var slash = target.IndexOf('/');
        var addressText = slash < 0 ? target : target[..slash];
        if (slash >= 0 && (slash == 0 || slash != target.LastIndexOf('/'))) return false;
        if (!IPAddress.TryParse(addressText, out var address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork
            && !string.Equals(addressText, address.ToString(), StringComparison.Ordinal)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6
            && (!addressText.Contains(':') || addressText.Contains('%') || addressText.Contains('[') || addressText.Contains(']'))) return false;
        if (slash < 0) return true;

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return int.TryParse(target[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            && prefix >= 0 && prefix <= maxPrefix;
    }
}
