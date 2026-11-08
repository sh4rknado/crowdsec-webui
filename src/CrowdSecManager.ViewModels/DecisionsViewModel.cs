using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class DecisionsViewModel : ViewModelBase
{
    private readonly ICrowdSecApiService _api;
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<DecisionsViewModel> _log;
    private string _ip = "";
    private string _duration = "4h";
    private string _type = "ban";
    private string _reason = "Manual SOC decision";
    private bool _bypassAllowlist;
    private IReadOnlyList<CsDecision> _decisions = [];
    private bool _isAdding;
    private bool _isDeletingAll;
    private string? _message;
    private bool _hasError;

    public DecisionsViewModel(ICrowdSecApiService api, ICrowdSecCliService cli, ILogger<DecisionsViewModel> log)
    { _api = api; _cli = cli; _log = log; }

    public string Ip { get => _ip; set => SetProperty(ref _ip, value); }
    public string Duration { get => _duration; set => SetProperty(ref _duration, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
    public bool BypassAllowlist { get => _bypassAllowlist; set => SetProperty(ref _bypassAllowlist, value); }
    public IReadOnlyList<CsDecision> Decisions { get => _decisions; private set => SetProperty(ref _decisions, value); }
    public bool IsAdding { get => _isAdding; private set => SetProperty(ref _isAdding, value); }
    public bool IsDeletingAll { get => _isDeletingAll; private set => SetProperty(ref _isDeletingAll, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

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
