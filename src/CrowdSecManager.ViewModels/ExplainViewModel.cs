using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class ExplainViewModel(ICrowdSecCliService cli, ILogger<ExplainViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly ILogger<ExplainViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));

    public string Type { get; set => SetProperty(ref field, value); } = "syslog";
    public string Labels { get; set => SetProperty(ref field, value); } = "";
    public string LogLine { get; set => SetProperty(ref field, value); } = "";
    public string FilePath { get; set => SetProperty(ref field, value); } = "";
    public bool Verbose { get; set => SetProperty(ref field, value); }
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public string? Output { get; private set => SetProperty(ref field, value); }
    public int ExitCode { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }

    public async Task ExplainLineAsync()
    {
        IsBusy = true; Message = null; Output = null;
        try
        {
            if (string.IsNullOrWhiteSpace(LogLine)) { HasError = true; Message = "Log line is required."; return; }
            if (string.IsNullOrWhiteSpace(Type)) { HasError = true; Message = "Acquisition type is required."; return; }
            var result = await _cli.ExplainLogLineAsync(LogLine, Type, NullIfBlank(Labels));
            SetResult(result);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Could not explain the log line"); Message = ex.Message; HasError = true; }
        finally { IsBusy = false; }
    }

    public async Task ExplainFileAsync()
    {
        IsBusy = true; Message = null; Output = null;
        try
        {
            if (string.IsNullOrWhiteSpace(FilePath)) { HasError = true; Message = "File path is required."; return; }
            if (string.IsNullOrWhiteSpace(Type)) { HasError = true; Message = "Acquisition type is required."; return; }
            var result = await _cli.ExplainLogFileAsync(FilePath, Type, Verbose);
            SetResult(result);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Could not explain the log file"); Message = ex.Message; HasError = true; }
        finally { IsBusy = false; }
    }

    public void ClearOutput() { Output = null; Message = null; ExitCode = 0; }

    private void SetResult(CliResult result)
    {
        ExitCode = result.ExitCode;
        HasError = !result.Success;
        Output = result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr);
        if (!result.Success && string.IsNullOrWhiteSpace(Output)) Output = $"Exit code {result.ExitCode}\n\n{result.FlattenError()}";
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
