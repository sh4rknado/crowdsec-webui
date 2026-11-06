namespace CrowdSecManager.Models;

public sealed class CsBouncer
{
    public string? Name { get; set; }
    public string? IpAddress { get; set; }
    public bool Revoked { get; set; }
    public string? ApiKey { get; set; }
    public string? Type { get; set; }
    public string? Version { get; set; }
    public DateTime? LastPull { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PullsCount { get; set; }
    public string? AuthType { get; set; }
}
