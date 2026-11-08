using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class DecisionsViewModel(ICrowdSecApiService api, ICrowdSecCliService cli, ILogger<DecisionsViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecApiService _api = api;
    private readonly ICrowdSecCliService _cli = cli;
    private readonly ILogger<DecisionsViewModel> _log = log;

    public string Ip { get; set => SetProperty(ref field, value); } = "";
    public string Duration { get; set => SetProperty(ref field, value); } = "4h";
    public string Type { get; set => SetProperty(ref field, value); } = "ban";
    public string Reason { get; set => SetProperty(ref field, value); } = "Manual SOC decision";
    public bool BypassAllowlist { get; private set => SetProperty(ref field, value); }
    public bool IsAdding { get; private set => SetProperty(ref field, value); }
    public bool IsDeletingAll { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }

    public IReadOnlyList<CsDecision> Decisions { get; private set => SetProperty(ref field, value); } = [];

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try { Decisions = await _api.GetDecisionsAsync(); }
        catch
        {
            try { Decisions = await _cli.ListDecisionsAsync(); }
            catch (Exception ex) { _log.LogDebug("decisions: {Msg}", ex.Message); }
        }
    }

    public async Task AddAsync()
    {
        IsAdding = true; Message = null;
        try
        {
            if (string.IsNullOrWhiteSpace(Ip)) { HasError = true; Message = "IP address is required."; return; }
            var result = await _cli.AddDecisionAsync(Ip, Duration, Reason, Type, BypassAllowlist);
            HasError = !result.Success;
            Message = result.Success
                ? $"Added decision for {Ip} ({Duration}, {Type}{(BypassAllowlist ? ", bypass allowlist" : "")})."
                : $"Failed: {result.FlattenError() ?? result.Stderr}";
            if (result.Success) { Ip = ""; BypassAllowlist = false; await RefreshAsync(); }
        }
        finally { IsAdding = false; }
    }

    public async Task DeleteAsync(long id)
    {
        var result = await _cli.DeleteDecisionAsync(id: id);
        if (result.Success) await RefreshAsync();
        else { HasError = true; Message = $"Failed: {result.FlattenError()}"; }
    }

    public async Task DeleteAllAsync()
    {
        IsDeletingAll = true;
        try
        {
            var result = await _cli.DeleteDecisionAsync(all: true);
            HasError = !result.Success;
            Message = result.Success ? "All decisions removed." : $"Failed: {result.FlattenError()}";
            await RefreshAsync();
        }
        finally { IsDeletingAll = false; }
    }

    public static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) ? value : value.Length > length ? value[..length] + "..." : value;
}
