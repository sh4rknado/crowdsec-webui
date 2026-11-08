namespace CrowdsecManagerModel;

/// <summary>Result of a <c>cscli</c> command execution.</summary>
public sealed class CliResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public TimeSpan Duration { get; set; }

    public bool Success => ExitCode == 0;

    public string? FlattenError()
    {
        if (Success) return null;
        return !string.IsNullOrWhiteSpace(Stderr) ? Stderr.Trim() : !string.IsNullOrWhiteSpace(Stdout) ? Stdout.Trim() : null;
    }
}
