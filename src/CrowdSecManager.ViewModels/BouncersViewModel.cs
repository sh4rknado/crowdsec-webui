using System.Text.RegularExpressions;
using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class BouncersViewModel(ICrowdSecCliService cli, ILogger<BouncersViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<BouncersViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public string NewName { get; set => SetProperty(ref field, value); } = "";
    public string NewKey { get; set => SetProperty(ref field, value); } = "";
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool IsAdding { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string? ApiKey { get; private set => SetProperty(ref field, value); }
    public string? InspectOutput { get; private set => SetProperty(ref field, value); }
    public string? InspectName { get; private set => SetProperty(ref field, value); }

    public IReadOnlyList<CsBouncer> Bouncers { get; private set => SetProperty(ref field, value); } = [];

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
            Message = result.Success ? "Pruned inactive bouncers." : $"Failed: {result.FlattenError() ?? result.Stderr}";
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
