namespace CrowdsecManagerModel;

public sealed class ThreatMapStats
{
    public long TotalAlerts { get; set; }
    public long TotalDecisions { get; set; }
    public long Last24hAlerts { get; set; }
    public Dictionary<string, int> TopCountries { get; set; } = [];
    public Dictionary<string, int> TopScenarios { get; set; } = [];
}
