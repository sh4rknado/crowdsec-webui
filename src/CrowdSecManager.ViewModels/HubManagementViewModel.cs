using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class HubManagementViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<HubManagementViewModel> _log;
    private IReadOnlyList<CsHubItem> _items = [];
    private string _filter = "";
    private string _searchQuery = "";
    private bool _isBusy;
    private bool _isBusyDiag;
    private string? _message;
    private bool _hasError;
    private string? _diagnosticOutput;
    private string _diagnosticTitle = "Output";

    public HubManagementViewModel(ICrowdSecCliService cli, ILogger<HubManagementViewModel> log) { _cli = cli; _log = log; }
    public IReadOnlyList<CsHubItem> Items { get => _items; private set => SetProperty(ref _items, value); }
    public string Filter { get => _filter; set => SetProperty(ref _filter, value); }
    public string SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsBusyDiag { get => _isBusyDiag; private set => SetProperty(ref _isBusyDiag, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string? DiagnosticOutput { get => _diagnosticOutput; private set => SetProperty(ref _diagnosticOutput, value); }
    public string DiagnosticTitle { get => _diagnosticTitle; private set => SetProperty(ref _diagnosticTitle, value); }
    public IEnumerable<CsHubItem> Filtered => string.IsNullOrWhiteSpace(Filter) ? Items : Items.Where(item =>
        (item.Name?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (item.Type?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (item.Description?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false));

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try { Items = await _cli.ListHubAsync(); }
        catch (Exception ex) { _log.LogDebug("hub list: {Msg}", ex.Message); }
    }

    public async Task UpdateAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.HubUpdateAsync();
            HasError = !result.Success;
            Message = result.Success ? $"Hub index updated in {result.Duration.TotalSeconds:F1}s." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task UpgradeAllAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.HubUpgradeAsync();
            HasError = !result.Success;
            Message = result.Success ? $"All items upgraded in {result.Duration.TotalSeconds:F1}s." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    public Task InstallAsync(CsHubItem item) => ApplyItemAsync(item, (type, name) => _cli.InstallItemAsync(type, name), $"Installed {item.Name}.");
    public Task RemoveAsync(CsHubItem item) => ApplyItemAsync(item, (type, name) => _cli.RemoveItemAsync(type, name), $"Removed {item.Name}.");
    public Task UpgradeAsync(CsHubItem item) => ApplyItemAsync(item, (type, name) => _cli.UpgradeItemAsync(type, name), $"Upgraded {item.Name}.");

    private async Task ApplyItemAsync(CsHubItem item, Func<string, string, Task<CliResult>> operation, string successMessage)
    {
        var result = await operation(item.Type ?? "collections", item.Name!);
        HasError = !result.Success;
        Message = result.Success ? successMessage : $"Failed: {result.FlattenError() ?? result.Stderr}";
        await RefreshAsync();
    }

    public async Task ReloadCrowdSecAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _cli.ReloadServiceAsync();
            HasError = !result.Success;
            Message = result.Success ? "Reload signal sent to the CrowdSec process in this container." : $"CrowdSec could not be reloaded from this container: {result.FlattenError() ?? result.Stderr}";
        }
        finally { IsBusy = false; }
    }

    public async Task HubSearchAsync()
    {
        IsBusyDiag = true;
        try
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) { HasError = true; Message = "Search query is required."; return; }
            var result = await _cli.HubSearchAsync(SearchQuery.Trim());
            DiagnosticTitle = $"cscli hub search \"{SearchQuery}\"";
            DiagnosticOutput = FormatResult(result);
        }
        finally { IsBusyDiag = false; }
    }

    public async Task HubBranchAsync()
    {
        IsBusyDiag = true;
        try { DiagnosticTitle = "cscli hub branch"; DiagnosticOutput = FormatResult(await _cli.HubBranchAsync()); }
        finally { IsBusyDiag = false; }
    }

    public async Task HubTypesAsync()
    {
        IsBusyDiag = true;
        try { DiagnosticTitle = "cscli hub types"; DiagnosticOutput = FormatResult(await _cli.HubTypesAsync()); }
        finally { IsBusyDiag = false; }
    }

    public static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) ? value : value.Length > length ? value[..length] + "..." : value;

    private static string FormatResult(CliResult result)
        => result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr) + $"\n--- exit code: {result.ExitCode} ---";
}
