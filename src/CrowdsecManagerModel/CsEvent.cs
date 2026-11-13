namespace CrowdsecManagerModel;

using System.Text.Json.Serialization;

public sealed class CsEvent
{
    public string? Timestamp { get; set; }
    public string? Type { get; set; }
    public List<CsMeta>? Meta { get; set; }
}
