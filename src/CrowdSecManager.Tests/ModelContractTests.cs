using System.Text.Json;
using System.Text.Json.Nodes;
using CrowdsecManagerModel;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ModelContractTests
{
    public static IEnumerable<object[]> ModelSamples()
    {
        var date = new DateTime(2026, 4, 12, 9, 30, 0, DateTimeKind.Utc);

        yield return new object[]
        {
            new ConfigFileInfo
            {
                Name = "app.yaml",
                RelativePath = "config/app.yaml",
                AbsolutePath = "/etc/crowdsec/app.yaml",
                Description = "Application config",
                SizeBytes = 512,
                LastModified = date,
                Content = "enabled: true"
            }
        };
        yield return new object[]
        {
            new CrowdSecOptions
            {
                CliPath = "/usr/bin/cscli",
                EnginePath = "/usr/bin/crowdsec",
                ConfigDir = "/etc/crowdsec",
                DataDir = "/var/lib/crowdsec",
                HubDir = "/etc/crowdsec/hub",
                LocalApiUrl = "http://localhost:8080",
                BouncerApiKey = "test-key",
                BouncerName = "test-bouncer",
                CommandTimeoutSeconds = 30,
                HubRefreshSeconds = 60
            }
        };
        yield return new object[]
        {
            new CsAlert
            {
                Id = 42,
                Source = new CsAlertSource { Scope = "Ip", Value = "203.0.113.1", Ip = "203.0.113.1" },
                CreatedAt = date,
                Until = date.AddHours(1),
                Scenario = "http-probing",
                ScenarioVersion = "1.2",
                ScenarioHash = "hash",
                Message = "alert message",
                BucketId = "bucket-1",
                EventsCount = 2,
                StartAt = "2026-04-12T09:00:00Z",
                StopAt = "2026-04-12T09:30:00Z",
                Events = [new CsEvent { Timestamp = "2026-04-12T09:00:00Z", Type = "http" }],
                Decisions = [new CsDecision { Id = 7, Type = "ban" }],
                Meta = [new CsMeta { Key = "country", Value = "BE" }]
            }
        };
        yield return new object[]
        {
            new CsBouncer
            {
                Name = "bouncer",
                IpAddress = "127.0.0.1",
                Revoked = true,
                ApiKey = "secret",
                Type = "crowdsec",
                Version = "1.0",
                LastPull = date,
                CreatedAt = date.AddDays(-1),
                PullsCount = 5,
                AuthType = "api-key"
            }
        };
        yield return new object[]
        {
            new CsDecision
            {
                Id = 9,
                Origin = "cscli",
                Scenario = "ssh-bf",
                Type = "ban",
                Scope = "ip",
                Value = "203.0.113.1",
                Duration = "4h",
                ExpiresAt = date.AddHours(4),
                CreatedAt = date,
                BanTenant = "tenant-1",
                Simulated = true,
                Reason = "brute force",
                Alerts = [new CsAlert { Id = 11 }]
            }
        };
        yield return new object[]
        {
            new CsEvent
            {
                Timestamp = "2026-04-12T09:00:00Z",
                Type = "http",
                Meta = [new CsMeta { Key = "country", Value = "BE" }, new CsMeta { Key = "count", Value = "2" }]
            }
        };
        yield return new object[]
        {
            new CsHubItem
            {
                Name = "collection/test",
                LocalVersion = "1.0",
                LocalPath = "/etc/crowdsec/collections/test.yaml",
                Description = "Test collection",
                LatestVersion = "1.1",
                Type = "collection",
                LocalHash = "hash",
                LocalHubItem = "hub-item",
                Status = "installed",
                Tainted = true,
                Installed = true,
                UpToDate = false
            }
        };
        yield return new object[]
        {
            new CsMachine
            {
                MachineId = "machine-1",
                IpAddress = "127.0.0.1",
                LastPush = date,
                LatestConfig = date.AddMinutes(-5),
                Version = 3,
                IsValidated = true,
                Status = "validated"
            }
        };
        yield return new object[] { new CsMeta { Key = "country", Value = "BE" } };
        yield return new object[]
        {
            new CsMetric
            {
                Name = "requests",
                Source = "api",
                Type = "counter",
                Current = 10,
                Max = 20,
                Min = 1,
                Avg = 5.5
            }
        };
        yield return new object[]
        {
            new CsNotification
            {
                Name = "email",
                Type = "email",
                Format = "text",
                ConfigPath = "/etc/crowdsec/notification.yaml",
                Description = "Email notifications",
                Enabled = true
            }
        };
        yield return new object[] { new CsSimulation { Scenario = "ssh-bf", Simulated = true, Mode = "enabled" } };
        yield return new object[] { new CsSimulationStatus { GloballyEnabled = true, RawOutput = "simulation enabled" } };
        yield return new object[]
        {
            new GeoIpOptions
            {
                DatabasePath = "/data/city.mmdb",
                HomeLatitude = 48.8566,
                HomeLongitude = 2.3522,
                HomeLabel = "Paris"
            }
        };
        yield return new object[]
        {
            new GeoPoint
            {
                Latitude = 50.8503,
                Longitude = 4.3517,
                Country = "Belgium",
                CountryIsoCode = "BE",
                City = "Brussels"
            }
        };
        yield return new object[]
        {
            new ThreatEvent
            {
                Id = "event-1",
                SourceIp = "203.0.113.1",
                Source = new GeoPoint { Latitude = 50.85, Longitude = 4.35 },
                Destination = new GeoPoint { Latitude = 48.85, Longitude = 2.35 },
                Scenario = "http-probing",
                Message = "suspicious request",
                AlertId = 42,
                CreatedAt = date,
                Severity = ThreatSeverity.High
            }
        };
        yield return new object[] { new ThreatFeedOptions { PollIntervalSeconds = 5, MaxActiveArcs = 30, ArcDurationMs = 1800 } };
        yield return new object[]
        {
            new ThreatMapSnapshot
            {
                Home = new GeoPoint { Latitude = 50.85, Longitude = 4.35, City = "Brussels" },
                Active = [new ThreatEvent { Id = "event-2", Severity = ThreatSeverity.Medium }],
                Stats = new ThreatMapStats
                {
                    TotalAlerts = 12,
                    TotalDecisions = 4,
                    Last24hAlerts = 3,
                    TopCountries = new Dictionary<string, int> { ["BE"] = 7 },
                    TopScenarios = new Dictionary<string, int> { ["ssh-bf"] = 5 }
                }
            }
        };
        yield return new object[]
        {
            new ThreatMapStats
            {
                TotalAlerts = 12,
                TotalDecisions = 4,
                Last24hAlerts = 3,
                TopCountries = new Dictionary<string, int> { ["BE"] = 7 },
                TopScenarios = new Dictionary<string, int> { ["ssh-bf"] = 5 }
            }
        };
        yield return new object[] { ThreatSeverity.Critical };
    }

    [Theory]
    [MemberData(nameof(ModelSamples))]
    public void ModelJsonRoundTrip_PreservesValues(object model)
    {
        var type = model.GetType();
        var json = JsonSerializer.Serialize(model, type);
        var deserialized = JsonSerializer.Deserialize(json, type);

        Assert.NotNull(deserialized);
        var roundTripJson = JsonSerializer.Serialize(deserialized, type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(roundTripJson)), type.Name);
    }

    [Fact]
    public void OptionsAndCollections_HaveExpectedDefaults()
    {
        var options = new CrowdSecOptions();
        Assert.Equal("/usr/local/bin/cscli", options.CliPath);
        Assert.Equal("/usr/local/bin/crowdsec", options.EnginePath);
        Assert.Equal("/etc/crowdsec", options.ConfigDir);
        Assert.Equal("/var/lib/crowdsec/data", options.DataDir);
        Assert.Equal("/etc/crowdsec/hub", options.HubDir);
        Assert.Equal("http://127.0.0.1:8080", options.LocalApiUrl);
        Assert.Equal("", options.BouncerApiKey);
        Assert.Equal("crowdsec-manager", options.BouncerName);
        Assert.Equal(120, options.CommandTimeoutSeconds);
        Assert.Equal(300, options.HubRefreshSeconds);

        var geoIpOptions = new GeoIpOptions();
        Assert.Equal("/var/lib/GeoIP/GeoLite2-City.mmdb", geoIpOptions.DatabasePath);
        Assert.Equal(50.8503, geoIpOptions.HomeLatitude);
        Assert.Equal(4.3517, geoIpOptions.HomeLongitude);
        Assert.Equal("Protected host", geoIpOptions.HomeLabel);

        var feedOptions = new ThreatFeedOptions();
        Assert.Equal(8, feedOptions.PollIntervalSeconds);
        Assert.Equal(60, feedOptions.MaxActiveArcs);
        Assert.Equal(3200, feedOptions.ArcDurationMs);

        Assert.Empty(new CsAlert().Events);
        Assert.Empty(new CsAlert().Decisions);
        Assert.Empty(new CsAlert().Meta);
        Assert.Empty(new CsDecision().Alerts);
        Assert.Empty(new ThreatMapStats().TopCountries);
        Assert.Empty(new ThreatMapStats().TopScenarios);
        Assert.NotNull(new ThreatMapSnapshot().Home);
        Assert.Empty(new ThreatMapSnapshot().Active);
        Assert.NotNull(new ThreatMapSnapshot().Stats);
    }

    [Fact]
    public void ThreatEvent_DefaultIds_AreUniqueGuidValues()
    {
        var first = new ThreatEvent();
        var second = new ThreatEvent();

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(Guid.TryParseExact(first.Id, "N", out _));
        Assert.True(Guid.TryParseExact(second.Id, "N", out _));
    }
}
