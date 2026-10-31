namespace CrowdSecManager.Models;

/// <summary>Alert as returned by <c>cscli alerts list -o json</c> / <c>GET /alerts</c>.</summary>
public sealed class CsAlert
{
    public long Id { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? Until { get; set; }
    public string? Scenario { get; set; }
    public string? ScenarioVersion { get; set; }
    public string? ScenarioHash { get; set; }
    public string? Message { get; set; }
    public string? BucketId { get; set; }
    public int EventsCount { get; set; }
    public string? StartAt { get; set; }
    public string? StopAt { get; set; }
    public List<CsEvent> Events { get; set; } = new();
    public List<CsDecision> Decisions { get; set; } = new();
    public List<CsMeta> Meta { get; set; } = new();
}

public sealed class CsEvent
{
    public long Timestamp { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, object?>? Meta { get; set; }
}

public sealed class CsMeta
{
    public string? Key { get; set; }
    public string? Value { get; set; }
}

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
    public List<CsAlert> Alerts { get; set; } = new();
}

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

/// <summary>Hub item as returned by <c>cscli hub list -o json</c>.</summary>
public sealed class CsHubItem
{
    public string? Name { get; set; }
    public string? LocalVersion { get; set; }
    public string? LocalPath { get; set; }
    public string? Description { get; set; }
    public string? LatestVersion { get; set; }
    public string? Type { get; set; }
    public string? LocalHash { get; set; }
    public string? LocalHubItem { get; set; }
    public string? Status { get; set; }
    public bool Tainted { get; set; }
    public bool Installed { get; set; }
    public bool UpToDate { get; set; }
}

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
