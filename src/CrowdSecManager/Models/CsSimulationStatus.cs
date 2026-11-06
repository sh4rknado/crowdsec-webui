namespace CrowdSecManager.Models;

/// <summary>Aggregated simulation status returned by <c>cscli simulation status</c>
/// (parsed from the human-form output - there is no JSON form for status).</summary>
public sealed class CsSimulationStatus
{
    public bool GloballyEnabled { get; set; }
    public string? RawOutput { get; set; }
}
