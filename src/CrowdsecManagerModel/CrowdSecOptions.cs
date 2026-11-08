namespace CrowdsecManagerModel;

public sealed class CrowdSecOptions
{
    public string CliPath { get; set; } = "/usr/local/bin/cscli";
    public string EnginePath { get; set; } = "/usr/local/bin/crowdsec";
    public string ConfigDir { get; set; } = "/etc/crowdsec";
    public string DataDir { get; set; } = "/var/lib/crowdsec/data";
    public string HubDir { get; set; } = "/etc/crowdsec/hub";
    public string LocalApiUrl { get; set; } = "http://127.0.0.1:8080";
    public string BouncerApiKey { get; set; } = "";
    public string BouncerName { get; set; } = "crowdsec-manager";
    public int CommandTimeoutSeconds { get; set; } = 120;
    public int HubRefreshSeconds { get; set; } = 300;
}
