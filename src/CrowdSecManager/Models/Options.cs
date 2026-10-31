namespace CrowdSecManager.Models;

/// <summary>Result of a <c>cscli</c> command execution.</summary>
public sealed class CliResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public TimeSpan Duration { get; set; }

    public bool Success => ExitCode == 0;

    public string? FlattenError()
        => string.IsNullOrWhiteSpace(Success ? null : Stderr) ? null : Stderr.Trim();
}

public sealed class ConfigFileInfo
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public string Description { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Content { get; set; } = "";
}

public sealed class CrowdSecOptions
{
    public string CliPath { get; set; } = "/usr/bin/cscli";
    public string EnginePath { get; set; } = "/usr/bin/crowdsec";
    public string ConfigDir { get; set; } = "/etc/crowdsec";
    public string DataDir { get; set; } = "/var/lib/crowdsec/data";
    public string HubDir { get; set; } = "/etc/crowdsec/hub";
    public string LocalApiUrl { get; set; } = "http://127.0.0.1:8080";
    public string BouncerApiKey { get; set; } = "";
    public string BouncerName { get; set; } = "crowdsec-manager";
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int HubRefreshSeconds { get; set; } = 300;
}

public sealed class GeoIpOptions
{
    public string DatabasePath { get; set; } = "/var/lib/GeoIP/GeoLite2-City.mmdb";
    public double HomeLatitude { get; set; } = 50.8503;
    public double HomeLongitude { get; set; } = 4.3517;
    public string HomeLabel { get; set; } = "Protected host";
}

public sealed class ThreatFeedOptions
{
    public int PollIntervalSeconds { get; set; } = 8;
    public int MaxActiveArcs { get; set; } = 60;
    public int ArcDurationMs { get; set; } = 3200;
}
