using CrowdSecManager.Data;
using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ThreatFeedServiceTests
{
    [Fact]
    public async Task ProcessBatch_PersistsPublishesAndDeduplicatesAlertsWithinLimit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var bus = new ThreatMapEventBus(capacity: 50);
        var service = new ThreatFeedService(
            provider,
            bus,
            new FixedGeoIpService(),
            Options.Create(new ThreatFeedOptions { MaxActiveArcs = 50 }),
            Options.Create(new GeoIpOptions()),
            provider.GetRequiredService<ILogger<ThreatFeedService>>());
        var alerts = Enumerable.Range(1, 25).Select(i => new CsAlert
        {
            Id = i,
            Source = $"203.0.113.{i}",
            Scenario = "crowdsecurity/ssh-bf",
            CreatedAt = DateTime.UtcNow.AddMinutes(-i)
        }).ToList();

        await service.ProcessBatchAsync(alerts, new[] { new CsDecision { Id = 9 } }, db);

        Assert.Equal(20, await db.ThreatHistory.CountAsync());
        Assert.Equal(20, bus.GetSnapshot().RecentEvents.Count);
        Assert.Equal(25, bus.GetSnapshot().Stats.TotalAlerts);
        Assert.All(bus.GetSnapshot().RecentEvents, e => Assert.Equal(ThreatSeverity.High, e.Severity));

        await service.ProcessBatchAsync(alerts, Array.Empty<CsDecision>(), db);

        Assert.Equal(25, await db.ThreatHistory.CountAsync());
        Assert.Equal(25, bus.GetSnapshot().RecentEvents.Count);

        await service.ProcessBatchAsync(alerts, Array.Empty<CsDecision>(), db);
        Assert.Equal(25, await db.ThreatHistory.CountAsync());
    }

    private sealed class FixedGeoIpService : IGeoIpService
    {
        public GeoPoint Home { get; } = new() { Latitude = 50, Longitude = 4, City = "Home" };
        public bool IsDatabaseAvailable => false;
        public GeoPoint Resolve(string? ip) => new()
        {
            Latitude = 10,
            Longitude = 20,
            City = "Test",
            Country = "Testland",
            CountryIsoCode = "TT"
        };
    }
}
