using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class LapiViewModel(ICrowdSecCliService cli, ILogger<LapiViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<LapiViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public string StatusOutput { get; private set => SetProperty(ref field, value); } = "";
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool IsRegistering { get; private set => SetProperty(ref field, value); }
    public bool IsLoadingContexts { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string RegisterMachine { get; private set => SetProperty(ref field, value); } = "";
    public string RegisterUrl { get; private set => SetProperty(ref field, value); } = "";
    public string RegisterToken { get; private set => SetProperty(ref field, value); } = "";
    public string? RegisterOutput { get; private set => SetProperty(ref field, value); }
    public string? ContextsOutput { get; private set => SetProperty(ref field, value); }

    public async Task RefreshAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.LapiStatusAsync();
            HasError = !result.Success;
            StatusOutput = FormatResult(result);
        }
        catch (Exception ex) { _log.LogDebug("lapi status: {Msg}", ex.Message); }
        finally { IsBusy = false; }
    }

    public async Task RegisterAsync()
    {
        IsRegistering = true; Message = null; RegisterOutput = null;
        try
        {
            if (string.IsNullOrWhiteSpace(RegisterUrl)) { HasError = true; Message = "LAPI URL is required."; return; }
            var result = await _cli.LapiRegisterAsync(NullIfBlank(RegisterMachine), NullIfBlank(RegisterUrl), NullIfBlank(RegisterToken));
            HasError = !result.Success;
            RegisterOutput = FormatResult(result);
            Message = result.Success
                ? "Registration sent. Check the output for the next steps (the LAPI host must validate the machine)."
                : $"Failed: {result.FlattenError() ?? result.Stderr}";
        }
        finally { IsRegistering = false; }
    }

    public async Task ListContextsAsync()
    {
        IsLoadingContexts = true; Message = null;
        try { ContextsOutput = FormatResult(await _cli.LapiContextListAsync()); }
        finally { IsLoadingContexts = false; }
    }

    private static string FormatResult(CliResult result)
        => result.Success
            ? result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr)
            : $"Exit code {result.ExitCode}\n\n{result.FlattenError() ?? result.Stderr}";

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
