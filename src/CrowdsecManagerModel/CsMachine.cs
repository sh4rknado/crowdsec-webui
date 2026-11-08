namespace CrowdsecManagerModel;

public sealed class CsMachine
{
    public string? MachineId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? LastPush { get; set; }
    public DateTime LatestConfig { get; set; }
    public int Version { get; set; }
    public bool IsValidated { get; set; }
    public string? Status { get; set; }
}
