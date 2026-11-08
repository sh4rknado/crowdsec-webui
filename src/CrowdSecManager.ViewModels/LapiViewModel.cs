using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class LapiViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<LapiViewModel> _log;
    private string _statusOutput = "";
    private bool _isBusy;
    private bool _isRegistering;
    private bool _isLoadingContexts;
    private string? _message;
    private bool _hasError;
    private string _registerMachine = "";
    private string _registerUrl = "";
    private string _registerToken = "";
    private string? _registerOutput;
    private string? _contextsOutput;

    public LapiViewModel(ICrowdSecCliService cli, ILogger<LapiViewModel> log) { _cli = cli; _log = log; }
    public string StatusOutput { get => _statusOutput; private set => SetProperty(ref _statusOutput, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsRegistering { get => _isRegistering; private set => SetProperty(ref _isRegistering, value); }
    public bool IsLoadingContexts { get => _isLoadingContexts; private set => SetProperty(ref _isLoadingContexts, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string RegisterMachine { get => _registerMachine; set => SetProperty(ref _registerMachine, value); }
    public string RegisterUrl { get => _registerUrl; set => SetProperty(ref _registerUrl, value); }
    public string RegisterToken { get => _registerToken; set => SetProperty(ref _registerToken, value); }
    public string? RegisterOutput { get => _registerOutput; private set => SetProperty(ref _registerOutput, value); }
    public string? ContextsOutput { get => _contextsOutput; private set => SetProperty(ref _contextsOutput, value); }

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
