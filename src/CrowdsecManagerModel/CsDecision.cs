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
    public string? Duration { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? BanTenant { get; set; }
    public bool Simulated { get; set; }
    public string? Reason { get; set; }
    public List<CsAlert> Alerts { get; set; } = [];
}
