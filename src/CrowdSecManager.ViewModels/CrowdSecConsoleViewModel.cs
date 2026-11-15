using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class CrowdSecConsoleViewModel(ICrowdSecCliService cli, ILogger<CrowdSecConsoleViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<CrowdSecConsoleViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string StatusOutput { get; private set => SetProperty(ref field, value); } = "";
    public string? Message { get; private set => SetProperty(ref field, value); }

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        IsBusy = true;
        Message = null;
        try
        {
            var result = await _cli.ConsoleStatusAsync();
            HasError = !result.Success;
            StatusOutput = FormatResult(result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read CrowdSec Console status");
            HasError = true;
            Message = $"Failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public Task EnableAlertsAsync() => SetAlertSharingAsync(enable: true);

    public Task DisableAlertsAsync() => SetAlertSharingAsync(enable: false);

    private async Task SetAlertSharingAsync(bool enable)
    {
        IsBusy = true;
        Message = null;
        try
        {
            var result = enable
                ? await _cli.ConsoleEnableAlertsAsync()
                : await _cli.ConsoleDisableAlertsAsync();

            HasError = !result.Success;
            Message = result.Success
                ? enable ? "Alert sharing enabled." : "Alert sharing disabled."
                : $"Failed: {result.FlattenError() ?? result.Stderr}";

            if (result.Success)
            {
                var status = await _cli.ConsoleStatusAsync();
                HasError = !status.Success;
                StatusOutput = FormatResult(status);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not {Action} CrowdSec Console alert sharing", enable ? "enable" : "disable");
            HasError = true;
            Message = $"Failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static string FormatResult(CliResult result)
        => result.Success
            ? result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr)
            : $"Exit code {result.ExitCode}\n\n{result.FlattenError() ?? result.Stderr}";
}
