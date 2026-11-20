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

    [Fact]
    public void DeserializesDecisionListAlertShape()
    {
        const string json = """
            [{
              "capacity": 0,
              "created_at": "2026-11-20T19:33:42Z",
              "decisions": [{ "duration": "1h29m23s", "id": 51577003, "origin": "crowdsec", "scenario": "crowdsecurity/http-open-proxy", "scope": "Ip", "simulated": false, "type": "ban", "value": "192.227.134.52" }],
              "events": [{ "meta": [{ "key": "source_ip", "value": "192.227.134.52" }], "timestamp": "2026-11-20 20:33:41 +0100 +0100" }],
              "events_count": 1,
              "id": 32850,
              "kind": "crowdsec",
              "leakspeed": "0s",
              "machine_id": "localhost",
              "remediation": true,
              "scenario": "crowdsecurity/http-open-proxy",
              "source": { "as_name": "AS-COLOCROSSING", "as_number": "36352", "cn": "US", "ip": "192.227.134.52", "latitude": 42.8864, "longitude": -78.8784, "range": "192.227.128.0/20", "scope": "Ip", "value": "192.227.134.52" },
              "start_at": "2026-11-20T19:33:41Z",
              "stop_at": "2026-11-20T19:33:41Z",
              "uuid": "59d47cb8-f0a6-4560-9530-8d2c98726b68"
            }]
            """;

        var records = JsonSerializer.Deserialize<List<CsDecision>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var record = Assert.Single(records!);
        var decision = Assert.Single(record.Decisions);

        Assert.Equal(32850, record.Id);
        Assert.Equal("192.227.134.52", record.Source?.Value);
        Assert.Equal(1, record.EventsCount);
        Assert.Equal(51577003, decision.Id);
        Assert.Equal("1h29m23s", decision.Duration);
        Assert.Equal("ban", decision.Type);
    }
}
