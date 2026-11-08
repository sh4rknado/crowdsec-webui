namespace CrowdsecManagerModel;

public sealed class ThreatFeedOptions
{
    public int PollIntervalSeconds { get; set; } = 8;
    public int MaxActiveArcs { get; set; } = 60;
    public int ArcDurationMs { get; set; } = 3200;
}
