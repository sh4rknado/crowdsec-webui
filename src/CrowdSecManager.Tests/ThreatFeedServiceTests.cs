using CrowdSecManager.Data;
using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ThreatFeedServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PollOnce_UsesLapiForDecisionsOrFallsBackToCli(bool lapiReachable)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var alerts = new[]
        {
            new CsAlert { Id = 77, Source = "203.0.113.77", Scenario = "crowdsecurity/ssh-bf", CreatedAt = DateTime.UtcNow }
        };
        var cliDecisions = new[] { new CsDecision { Id = 5 } };
        var apiDecisions = new[] { new CsDecision { Id = 8 } };
        var api = new FixedApi(lapiReachable, apiDecisions);
        var cli = CreateCliProxy(alerts, cliDecisions, out var cliState);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ICrowdSecApiService>(api);
        services.AddSingleton(cli);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        var bus = new ThreatMapEventBus();
        var service = CreateService(provider, bus);
        await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, cliState.AlertCalls);
        Assert.Equal(lapiReachable ? 0 : 1, cliState.DecisionCalls);
        Assert.Equal(lapiReachable ? 1 : 0, api.DecisionCalls);
        Assert.Equal(1, bus.GetSnapshot().Stats.TotalDecisions);
        await using var verifyScope = provider.CreateAsyncScope();
        Assert.Equal(1, await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().ThreatHistory.CountAsync());
    }

    [Fact]
    public async Task PollOnce_PropagatesCancellationFromLapiProbe()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ICrowdSecApiService>(new FixedApi(true, Array.Empty<CsDecision>()));
        services.AddSingleton(CreateCliProxy(Array.Empty<CsAlert>(), Array.Empty<CsDecision>(), out _));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        var service = CreateService(provider, new ThreatMapEventBus());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PollOnceAsync(cancellation.Token));
    }

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
        var service = CreateService(provider, bus, 50);
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

        var restartedBus = new ThreatMapEventBus();
        var restartedService = CreateService(provider, restartedBus, 50);
        await restartedService.ProcessBatchAsync(alerts, Array.Empty<CsDecision>(), db);
        Assert.Empty(restartedBus.GetSnapshot().RecentEvents);
        Assert.Equal(25, await db.ThreatHistory.CountAsync());
    }

    [Fact]
    public async Task ProcessBatch_PublishesUpdatedStatsWhenThereAreNoAlerts()
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
        var bus = new ThreatMapEventBus();
        var service = CreateService(provider, bus);

        await service.ProcessBatchAsync(Array.Empty<CsAlert>(), new[]
        {
            new CsDecision { Id = 1 }, new CsDecision { Id = 2 }
        }, db);

        Assert.Equal(0, bus.GetSnapshot().Stats.TotalAlerts);
        Assert.Equal(2, bus.GetSnapshot().Stats.TotalDecisions);
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

    private static ThreatFeedService CreateService(IServiceProvider provider, IThreatMapEventBus bus, int maxArcs = 60)
        => new(provider, bus, new FixedGeoIpService(),
            Options.Create(new ThreatFeedOptions { MaxActiveArcs = maxArcs }),
            Options.Create(new GeoIpOptions()), provider.GetRequiredService<ILogger<ThreatFeedService>>());

    private static ICrowdSecCliService CreateCliProxy(IReadOnlyList<CsAlert> alerts,
        IReadOnlyList<CsDecision> decisions, out CliProxy state)
    {
        var proxy = DispatchProxy.Create<ICrowdSecCliService, CliProxy>();
        state = (CliProxy)(object)proxy;
        state.Alerts = alerts;
        state.Decisions = decisions;
        return proxy;
    }

    private sealed class FixedApi(bool reachable, IReadOnlyList<CsDecision> decisions) : ICrowdSecApiService
    {
        public int DecisionCalls { get; private set; }
        public Task<bool> IsReachableAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(reachable);
        }
        public Task<IReadOnlyList<CsDecision>> GetDecisionsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DecisionCalls++;
            return Task.FromResult(decisions);
        }
        public Task<IReadOnlyList<CsAlert>> GetAlertsAsync(int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CsAlert>>(Array.Empty<CsAlert>());
        public Task<ThreatMapStats> GetSnapshotStatsAsync(CancellationToken ct = default)
            => Task.FromResult(new ThreatMapStats());
    }

    public class CliProxy : DispatchProxy
    {
        internal IReadOnlyList<CsAlert> Alerts { get; set; } = Array.Empty<CsAlert>();
        internal IReadOnlyList<CsDecision> Decisions { get; set; } = Array.Empty<CsDecision>();
        internal int AlertCalls { get; private set; }
        internal int DecisionCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                nameof(ICrowdSecCliService.ListAlertsAsync) => ListAlerts(),
                nameof(ICrowdSecCliService.ListDecisionsAsync) => ListDecisions(),
                _ => throw new NotSupportedException($"Unexpected CLI call: {targetMethod?.Name}")
            };

        private Task<IReadOnlyList<CsAlert>> ListAlerts()
        {
            AlertCalls++;
            return Task.FromResult(Alerts);
        }

        private Task<IReadOnlyList<CsDecision>> ListDecisions()
        {
            DecisionCalls++;
            return Task.FromResult(Decisions);
        }
    }
}
