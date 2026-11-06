namespace CrowdSecManager.Models;

public sealed class CsMetric
{
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? Type { get; set; }
    public long Current { get; set; }
    public long? Max { get; set; }
    public long? Min { get; set; }
    public double? Avg { get; set; }
}
