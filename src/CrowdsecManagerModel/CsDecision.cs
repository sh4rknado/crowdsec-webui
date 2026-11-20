namespace CrowdsecManagerModel;

/// <summary>Decision as returned by <c>cscli decisions list -o json</c> / <c>GET /decisions</c>.</summary>
public sealed class CsDecision
{
    public long Id { get; set; }
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
