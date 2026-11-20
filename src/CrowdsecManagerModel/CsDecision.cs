namespace CrowdsecManagerModel;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Decision record returned by <c>cscli decisions list -o json</c>.</summary>
/// <remarks>
/// CrowdSec returns an alert-shaped record whose actual decisions are nested in
/// <see cref="Decisions"/>. The nested decision objects use this same contract
/// for the decision-specific fields.
/// </remarks>
public sealed class CsDecision
{
    // Decision fields (also used by the objects in the decisions array).
    public long Id { get; set; }
    [JsonPropertyName("alert_id")]
    public long? AlertId { get; set; }
    [JsonPropertyName("alertId")]
    public long? AlertIdCamelCase { get => AlertId; set { if (value.HasValue) AlertId = value; } }
    public string? Origin { get; set; }
    public string? Scenario { get; set; }
    public string? Type { get; set; }
    public string? Scope { get; set; }
    public string? Value { get; set; }
    public string? Duration { get; set; }
    public string? Until { get; set; }
    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }
    [JsonPropertyName("ban_tenant")]
    public string? BanTenant { get; set; }
    public bool Simulated { get; set; }
    public string? Reason { get; set; }

    // Alert-shaped fields returned by decisions list.
    public int Capacity { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    public List<CsDecision> Decisions { get; set; } = [];
    public List<CsEvent> Events { get; set; } = [];
    [JsonPropertyName("events_count")]
    public int EventsCount { get; set; }
    public string? Kind { get; set; }
    public JsonElement? Labels { get; set; }
    public string? Leakspeed { get; set; }
    [JsonPropertyName("machine_id")]
    public string? MachineId { get; set; }
    public string? Message { get; set; }
    public List<CsMeta> Meta { get; set; } = [];
    public bool Remediation { get; set; }
    [JsonPropertyName("scenario_hash")]
    public string? ScenarioHash { get; set; }
    [JsonPropertyName("scenario_version")]
    public string? ScenarioVersion { get; set; }
    public CsAlertSource? Source { get; set; }
    [JsonPropertyName("start_at")]
    public string? StartAt { get; set; }
    [JsonPropertyName("stop_at")]
    public string? StopAt { get; set; }
    public string? Uuid { get; set; }
    [JsonIgnore]
    public List<CsAlert> Alerts { get; set; } = [];
}
