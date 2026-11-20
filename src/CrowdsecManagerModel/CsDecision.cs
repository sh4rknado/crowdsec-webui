namespace CrowdsecManagerModel;

using System.Text.Json.Serialization;

/// <summary>Decision as returned by <c>cscli decisions list -o json</c> / <c>GET /decisions</c>.</summary>
public sealed class CsDecision
{
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
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
    [JsonPropertyName("until")]
    public string? Until { get; set; }
    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("ban_tenant")]
    public string? BanTenant { get; set; }
    public bool Simulated { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    public List<CsAlert> Alerts { get; set; } = [];
}
