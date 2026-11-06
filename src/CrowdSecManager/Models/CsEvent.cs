namespace CrowdSecManager.Models;

public sealed class CsEvent
{
    public long Timestamp { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, object?>? Meta { get; set; }
}
