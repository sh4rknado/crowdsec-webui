using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ThreatMapEventReplayBufferTests
{
    [Fact]
    public void Drain_MergesSnapshotAndPreInitializationEventsWithoutDuplicates()
    {
        var buffer = new ThreatMapEventReplayBuffer();
        var old = new ThreatEvent { AlertId = 1, CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        var overlapSnapshot = new ThreatEvent { AlertId = 2, CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
        var overlapPending = new ThreatEvent { AlertId = 2, CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
        var liveOnly = new ThreatEvent { AlertId = 3, CreatedAt = DateTime.UtcNow };
        buffer.Enqueue(overlapPending);
        buffer.Enqueue(liveOnly);

        var result = buffer.Drain(new[] { old, overlapSnapshot });

        Assert.Equal(new long[] { 1, 2, 3 }, result.Select(threat => threat.AlertId));
        Assert.Same(overlapSnapshot, result.Single(threat => threat.AlertId == 2));
        Assert.Empty(buffer.Drain());
    }

    [Fact]
    public void Enqueue_KeepsOnlyTheNewestEventsWhenCapacityIsReached()
    {
        var buffer = new ThreatMapEventReplayBuffer(capacity: 2);
        buffer.Enqueue(new ThreatEvent { AlertId = 1 });
        buffer.Enqueue(new ThreatEvent { AlertId = 2 });
        buffer.Enqueue(new ThreatEvent { AlertId = 3 });

        Assert.Equal(new long[] { 2, 3 }, buffer.Drain().Select(threat => threat.AlertId));
    }

    [Fact]
    public void Drain_ReturnsPendingEventsQueuedWhilePreviousDrainWasBeingPlotted()
    {
        var buffer = new ThreatMapEventReplayBuffer();
        buffer.Enqueue(new ThreatEvent { AlertId = 1 });

        Assert.Single(buffer.Drain());

        buffer.Enqueue(new ThreatEvent { AlertId = 2 });
        Assert.Equal(2, Assert.Single(buffer.Drain()).AlertId);
    }
}
