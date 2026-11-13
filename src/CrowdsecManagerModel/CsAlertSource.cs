using System.Text.Json.Serialization;

namespace CrowdsecManagerModel;

/// <summary>Source object returned by CrowdSec for an alert.</summary>
public sealed class CsAlertSource
{
    public string? Scope { get; set; }
    public string? Value { get; set; }
    public string? Ip { get; set; }
    public string? Range { get; set; }
    [JsonPropertyName("as_number")]
    public string? AsNumber { get; set; }
    [JsonPropertyName("as_name")]
    public string? AsName { get; set; }
    public string? Cn { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
