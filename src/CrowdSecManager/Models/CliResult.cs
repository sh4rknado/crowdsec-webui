namespace CrowdSecManager.Models;

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
        if (!string.IsNullOrWhiteSpace(Stderr)) return Stderr.Trim();
        if (!string.IsNullOrWhiteSpace(Stdout)) return Stdout.Trim();
        return null;
    }
}
