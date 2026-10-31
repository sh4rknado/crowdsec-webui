using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ThreatMapEventBusTests
{
    [Fact]
    public void PublishThreat_NotifiesSubscribersAndRetainsBoundedSnapshot()
    {
        var bus = new ThreatMapEventBus(capacity: 2);
        var notifications = new List<long>();
        bus.ThreatSpotted += (threat, _) => notifications.Add(threat.AlertId);

        bus.PublishThreat(new ThreatEvent { AlertId = 1 }, new ThreatMapStats { TotalAlerts = 1 });
        bus.PublishThreat(new ThreatEvent { AlertId = 2 }, new ThreatMapStats { TotalAlerts = 2 });
        bus.PublishThreat(new ThreatEvent { AlertId = 3 }, new ThreatMapStats { TotalAlerts = 3 });

        var snapshot = bus.GetSnapshot();
        Assert.Equal(new long[] { 1, 2, 3 }, notifications);
        Assert.Equal(new long[] { 2, 3 }, snapshot.RecentEvents.Select(x => x.AlertId));
        Assert.Equal(3, snapshot.Stats.TotalAlerts);
    }

    [Fact]
    public void PublishStats_NotifiesSubscribersAndUpdatesSnapshot()
    {
        var bus = new ThreatMapEventBus();
        ThreatMapStats? notification = null;
        bus.StatsUpdated += stats => notification = stats;
        var stats = new ThreatMapStats { TotalDecisions = 7 };

        bus.PublishStats(stats);

        Assert.Same(stats, notification);
        Assert.Same(stats, bus.GetSnapshot().Stats);
    }
}
