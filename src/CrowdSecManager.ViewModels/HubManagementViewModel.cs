using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class HubManagementViewModel(ICrowdSecCliService cli, ILogger<HubManagementViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<HubManagementViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public string Filter { get; set => SetProperty(ref field, value); } = "";
    public string SearchQuery { get; set => SetProperty(ref field, value); } = "";
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool IsBusyDiag { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string? DiagnosticOutput { get; private set => SetProperty(ref field, value); }
    public string DiagnosticTitle { get; private set => SetProperty(ref field, value); } = "Output";

    public IReadOnlyList<CsHubItem> Items { get; private set => SetProperty(ref field, value); } = [];

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
