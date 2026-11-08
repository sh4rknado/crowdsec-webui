namespace CrowdsecManagerModel;

/// <summary>Scenario entry returned by <c>cscli simulation list -o json</c>.</summary>
public sealed class CsSimulation
{
    public string? Scenario { get; set; }
    public bool Simulated { get; set; }
    public string? Mode { get; set; }
}
