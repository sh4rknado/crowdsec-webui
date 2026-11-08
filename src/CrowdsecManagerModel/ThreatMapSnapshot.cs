namespace CrowdsecManagerModel;

public sealed class ThreatMapSnapshot
{
    public GeoPoint Home { get; set; } = new();
    public List<ThreatEvent> Active { get; set; } = new();
    public ThreatMapStats Stats { get; set; } = new();
}
