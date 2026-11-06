namespace CrowdSecManager.Models;

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
