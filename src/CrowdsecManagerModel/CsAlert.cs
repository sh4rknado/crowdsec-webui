namespace CrowdsecManagerModel;

/// <summary>Alert as returned by <c>cscli alerts list -o json</c> / <c>GET /alerts</c>.</summary>
public sealed class CsAlert
{
    public long Id { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? Until { get; set; }
    public string? Scenario { get; set; }
    public string? ScenarioVersion { get; set; }
    public string? ScenarioHash { get; set; }
    public string? Message { get; set; }
    public string? BucketId { get; set; }
    public int EventsCount { get; set; }
    public string? StartAt { get; set; }
    public string? StopAt { get; set; }
    public List<CsEvent> Events { get; set; } = [];
    public List<CsDecision> Decisions { get; set; } = [];
    public List<CsMeta> Meta { get; set; } = [];
}
