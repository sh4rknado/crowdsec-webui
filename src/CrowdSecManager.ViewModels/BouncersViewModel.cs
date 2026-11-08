using System.Text.RegularExpressions;
using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class BouncersViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<BouncersViewModel> _log;
    private string _newName = "";
    private string _newKey = "";
    private IReadOnlyList<CsBouncer> _bouncers = [];
    private bool _isBusy;
    private bool _isAdding;
    private string? _message;
    private bool _hasError;
    private string? _apiKey;
    private string? _inspectOutput;
    private string? _inspectName;

    public BouncersViewModel(ICrowdSecCliService cli, ILogger<BouncersViewModel> log) { _cli = cli; _log = log; }

    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }
    public string NewKey { get => _newKey; set => SetProperty(ref _newKey, value); }
    public IReadOnlyList<CsBouncer> Bouncers { get => _bouncers; private set => SetProperty(ref _bouncers, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsAdding { get => _isAdding; private set => SetProperty(ref _isAdding, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string? ApiKey { get => _apiKey; private set => SetProperty(ref _apiKey, value); }
    public string? InspectOutput { get => _inspectOutput; private set => SetProperty(ref _inspectOutput, value); }
    public string? InspectName { get => _inspectName; private set => SetProperty(ref _inspectName, value); }

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try { Bouncers = await _cli.ListBouncersAsync(); }
        catch (Exception ex) { _log.LogDebug("bouncers: {Msg}", ex.Message); }
    }

    public async Task AddAsync()
    {
        IsAdding = true; Message = null; ApiKey = null;
        try
        {
            if (string.IsNullOrWhiteSpace(NewName)) { HasError = true; Message = "Name is required."; return; }
            var result = await _cli.AddBouncerAsync(NewName, NullIfBlank(NewKey));
            HasError = !result.Success;
            if (result.Success)
            {
                ApiKey = string.IsNullOrWhiteSpace(NewKey) ? ExtractApiKey(result.Stdout) : NewKey;
                Message = ApiKey is null ? "Bouncer registered." : null;
                NewName = NewKey = "";
                await RefreshAsync();
            }
            else Message = $"Failed: {result.FlattenError() ?? result.Stderr}";
        }
        finally { IsAdding = false; }
    }

    public async Task DeleteAsync(string name)
    {
        var result = await _cli.DeleteBouncerAsync(name);
        HasError = !result.Success;
        Message = result.Success ? $"Revoked {name}." : $"Failed: {result.FlattenError()}";
        await RefreshAsync();
    }

    public async Task InspectAsync(string name)
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.InspectBouncerAsync(name);
            InspectName = name;
            InspectOutput = result.Success
                ? result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr)
                : $"Exit code {result.ExitCode}\n\n{result.FlattenError() ?? result.Stderr}";
        }
        finally { IsBusy = false; }
    }

    public async Task PruneAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.PruneBouncersAsync();
            HasError = !result.Success;
            Message = result.Success ? "Pruned revoked bouncers." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractApiKey(string stdout)
    {
        var match = Regex.Match(stdout, @"Api key[^:]*:\s*\n?\s*([A-Za-z0-9/+=]+)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
