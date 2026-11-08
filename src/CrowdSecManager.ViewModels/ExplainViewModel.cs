using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class ExplainViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<ExplainViewModel> _log;
    private string _type = "syslog";
    private string _labels = "";
    private string _logLine = "";
    private string _filePath = "";
    private bool _verbose;
    private bool _isBusy;
    private string? _output;
    private int _exitCode;
    private string? _message;
    private bool _hasError;

    public ExplainViewModel(ICrowdSecCliService cli, ILogger<ExplainViewModel> log) { _cli = cli; _log = log; }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Labels { get => _labels; set => SetProperty(ref _labels, value); }
    public string LogLine { get => _logLine; set => SetProperty(ref _logLine, value); }
    public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
    public bool Verbose { get => _verbose; set => SetProperty(ref _verbose, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Output { get => _output; private set => SetProperty(ref _output, value); }
    public int ExitCode { get => _exitCode; private set => SetProperty(ref _exitCode, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

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
