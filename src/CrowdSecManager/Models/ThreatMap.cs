namespace CrowdSecManager.Models;

/// <summary>Geographic point used by the threat-map renderer.</summary>
public sealed class GeoPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Country { get; set; }
    public string? CountryIsoCode { get; set; }
    public string? City { get; set; }
}

public sealed class ThreatEvent
{
    /// <summary>Stable id used by the canvas to dedupe.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceIp { get; set; }
    public GeoPoint? Source { get; set; }
    public GeoPoint? Destination { get; set; }
    public string? Scenario { get; set; }
    public string? Message { get; set; }
    public long AlertId { get; set; }
    public DateTime CreatedAt { get; set; }
    public ThreatSeverity Severity { get; set; }
}

public enum ThreatSeverity { Info, Low, Medium, High, Critical }

public sealed class ThreatMapSnapshot
{
    public GeoPoint Home { get; set; } = new();
    public List<ThreatEvent> Active { get; set; } = new();
    public ThreatMapStats Stats { get; set; } = new();
}

public sealed class ThreatMapStats
{
    public long TotalAlerts { get; set; }
    public long TotalDecisions { get; set; }
    public long Last24hAlerts { get; set; }
    public Dictionary<string, int> TopCountries { get; set; } = new();
    public Dictionary<string, int> TopScenarios { get; set; } = new();
}
