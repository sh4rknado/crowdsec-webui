namespace CrowdsecManagerModel;

using System.Text.Json.Serialization;

/// <summary>Alert as returned by <c>cscli alerts list -o json</c> / <c>GET /alerts</c>.</summary>
public sealed class CsAlert
{
    public long Id { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    public DateTime? Until { get; set; }
    public string? Scenario { get; set; }
    [JsonPropertyName("scenario_version")]
    public string? ScenarioVersion { get; set; }
    [JsonPropertyName("scenario_hash")]
    public string? ScenarioHash { get; set; }
    public string? Message { get; set; }
    [JsonPropertyName("bucket_id")]
    public string? BucketId { get; set; }
    [JsonPropertyName("events_count")]
    public int EventsCount { get; set; }
    [JsonPropertyName("start_at")]
    public string? StartAt { get; set; }
    [JsonPropertyName("stop_at")]
    public string? StopAt { get; set; }
    public List<CsEvent> Events { get; set; } = [];
    public List<CsDecision> Decisions { get; set; } = [];
    public List<CsMeta> Meta { get; set; } = [];
    [JsonPropertyName("source")]
    public CsAlertSource? Source { get; set; }
}
