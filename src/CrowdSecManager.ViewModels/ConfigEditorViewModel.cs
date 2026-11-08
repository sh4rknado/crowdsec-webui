using CrowdSecManager.Services;
using CrowdsecManagerModel;

namespace CrowdSecManager.ViewModels;

public sealed class ConfigEditorViewModel : ViewModelBase
{
    private readonly IConfigFileService _config;
    private readonly ICrowdSecCliService _cli;
    private IReadOnlyList<ConfigFileInfo> _files = [];
    private ConfigFileInfo? _current;
    private string _draft = "";
    private bool _isBusy;
    private bool _isBusyConfig;
    private string? _message;
    private bool _hasError;
    private string? _configOutput;
    private string _configOutputTitle = "Output";
    private string _selected = "";
    private string _newFilePath = "";

    public ConfigEditorViewModel(IConfigFileService config, ICrowdSecCliService cli) { _config = config; _cli = cli; }
    public IReadOnlyList<ConfigFileInfo> Files { get => _files; private set => SetProperty(ref _files, value); }
    public ConfigFileInfo? Current { get => _current; private set => SetProperty(ref _current, value); }
    public string Draft { get => _draft; set => SetProperty(ref _draft, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsBusyConfig { get => _isBusyConfig; private set => SetProperty(ref _isBusyConfig, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string? ConfigOutput { get => _configOutput; private set => SetProperty(ref _configOutput, value); }
    public string ConfigOutputTitle { get => _configOutputTitle; private set => SetProperty(ref _configOutputTitle, value); }
    public string Selected { get => _selected; private set => SetProperty(ref _selected, value); }
    public string NewFilePath { get => _newFilePath; set => SetProperty(ref _newFilePath, value); }

    public override Task InitializeAsync()
    {
        Files = _config.ListConfigFiles();
        return Task.CompletedTask;
    }

    public Task SelectAsync(string relativePath)
    {
        Selected = relativePath;
        Current = _config.Load(relativePath);
        Draft = Current?.Content ?? "";
        Message = null;
        return Task.CompletedTask;
    }

    public async Task CreateNewFileAsync()
    {
        var path = NewFilePath.Trim().TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(path)) return;
        var (created, error) = _config.TryCreate(path, out var detail);
        HasError = !created;
        Message = created
            ? $"Created {path}. It will be picked up by CrowdSec on next reload (use 'Apply / reload CrowdSec')."
            : detail ?? error;
        if (created)
        {
            Files = _config.ListConfigFiles();
            NewFilePath = "";
            await SelectAsync(path);
        }
    }

    public Task ReloadAsync()
    {
        if (Current is null) return Task.CompletedTask;
        Current = _config.Load(Current.RelativePath);
        Draft = Current?.Content ?? "";
        Message = "Reloaded from disk.";
        HasError = false;
        return Task.CompletedTask;
    }

    public Task ValidateAsync()
    {
        var (valid, error) = _config.ValidateYaml(Draft);
        HasError = !valid;
        Message = valid ? "YAML is valid." : error;
        return Task.CompletedTask;
    }

    public void Save()
    {
        if (Current is null) return;
        IsBusy = true; Message = null;
        try
        {
            var (valid, error) = _config.Save(Current.RelativePath, Draft);
            HasError = !valid;
            Message = valid ? "Saved. Apply or reload CrowdSec to load the change." : error;
            if (valid) Current = _config.Load(Current.RelativePath);
        }
        finally { IsBusy = false; }
    }

    public async Task ApplyToCrowdSecAsync()
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

    public async Task ValidateWithCrowdSecAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _cli.ValidateEngineConfigAsync();
            HasError = !result.Success;
            Message = result.Success ? "CrowdSec accepted the saved configuration." : $"CrowdSec configuration test failed: {result.FlattenError() ?? result.Stderr ?? result.Stdout}";
        }
        finally { IsBusy = false; }
    }

    public Task ShowMergedAsync() => ShowCommandOutputAsync(_cli.ConfigShowAsync(), "cscli config show");
    public Task ShowMergedYamlAsync() => ShowCommandOutputAsync(_cli.ConfigShowYamlAsync(), "cscli config show-yaml");
    public Task ShowFeatureFlagsAsync() => ShowCommandOutputAsync(_cli.ConfigFeatureFlagsAsync(), "cscli config feature-flags");

    private async Task ShowCommandOutputAsync(Task<CliResult> operation, string title)
    {
        IsBusyConfig = true;
        try { ConfigOutputTitle = title; ConfigOutput = FormatResult(await operation); }
        finally { IsBusyConfig = false; }
    }

    public async Task BackupConfigAsync()
    {
        IsBusyConfig = true; Message = null;
        try
        {
            var target = $"/var/lib/crowdsec-manager/backups/cscli-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.tgz";
            var result = await _cli.ConfigBackupAsync(target);
            HasError = !result.Success;
            Message = result.Success
                ? $"Backup written to {target} (in the container; bind-mounted on the host at the same path)."
                : $"Backup failed: {result.FlattenError() ?? result.Stderr}";
            ConfigOutputTitle = "cscli config backup";
            ConfigOutput = FormatResult(result);
        }
        finally { IsBusyConfig = false; }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        0 => "—",
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("F1") + " KiB",
        _ => (bytes / 1024.0 / 1024).ToString("F1") + " MiB"
    };

    public static string FormatDate(DateTime date) => date == DateTime.MinValue ? "—" : date.ToString("yyyy-MM-dd HH:mm");

    public static bool IsHubManaged(string path)
        => path.StartsWith("parsers/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("scenarios/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("postoverflows/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("collections/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("contexts/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("appsec-rules/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("appsec-configs/", StringComparison.OrdinalIgnoreCase);

    private static string FormatResult(CliResult result)
        => result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr) + $"\n--- exit code: {result.ExitCode} ---";
}
