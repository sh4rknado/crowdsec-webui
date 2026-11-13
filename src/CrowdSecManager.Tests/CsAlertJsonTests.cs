using System.Text.Json;
using CrowdsecManagerModel;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CsAlertJsonTests
{
    [Fact]
    public void DeserializesCrowdSec18AlertJsonWithObjectSourceAndEventMetadata()
    {
        const string json = """
            [{
              "id": 42,
              "created_at": "2026-11-13T16:50:47Z",
              "scenario": "crowdsecurity/ssh-bf",
              "events_count": 3,
              "source": { "scope": "Ip", "value": "203.0.113.8", "ip": "203.0.113.8", "cn": "BE", "as_name": "Example ISP" },
              "events": [{ "timestamp": "2026-11-13T16:49:00Z", "meta": [{ "key": "source_ip", "value": "203.0.113.8" }] }],
              "decisions": []
            }]
            """;

        var alerts = JsonSerializer.Deserialize<List<CsAlert>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var alert = Assert.Single(alerts!);
        Assert.Equal(42, alert.Id);
        Assert.Equal(3, alert.EventsCount);
        Assert.Equal("203.0.113.8", alert.Source?.Value);
        Assert.Equal("BE", alert.Source?.Cn);
        Assert.Equal("Example ISP", alert.Source?.AsName);
        Assert.Equal("203.0.113.8", Assert.Single(alert.Events).Meta?.Single().Value);
    }
}
