using System.Text.Json.Serialization;

namespace CrowdsecManagerModel;

public sealed class CsMachine
{
    public string? MachineId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? LastUpdate { get; set; }
    [JsonPropertyName("os")]
    public string? OperatingSystem { get; set; }
    public string? AuthType { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public string[]? Features { get; set; }

    // Kept for compatibility with CrowdSec versions exposing these older fields.
    public DateTime? LastPush { get; set; }
    public DateTime LatestConfig { get; set; }
    public string? Version { get; set; }
    public bool IsValidated { get; set; }
    public string? Status { get; set; }
}
