// Copyright (c) CrowdSec Manager - Tests unitaires supplémentaires pour les ViewModels UI

using CrowdSecManager.ViewModels;
using CrowdSecManager.Services.Mocks;
using Moq;
using Xunit;

namespace CrowdSecManager.Tests;

public class DashboardVisualViewModelTests
{
    private readonly Mock<ICrowdSecApiService> _apiMock = new();
    private readonly Mock<ICrowdSecCliService> _cliMock = new();
    private readonly Mock<IConfigFileService> _configMock = new();
    private readonly Mock<IGeoIpService> _geoMock = new();

    [Fact]
    public async Task Refresh_LoadsStatsDecisionsAndGeoAvailability()
    {
        // Arrange
        var stats = new ThreatMapStats 
        { 
            TotalAlerts = 500, 
            DecidedRate = 98.5f, 
            RateLimited = 120,
            UniqueSources = 342
        };
        _apiMock.Setup(a => a.GetSnapshotStatsAsync()).ReturnsAsync(stats);
        _apiMock.Setup(a => a.GetDecisionsAsync())
            .ReturnsAsync(new List<CsDecision>
            {
                new CsDecision { Id = 1, Target = "192.168.1.0/24", Reason = "malicious", Until = DateTime.UtcNow.AddHours(4) }
            });
        _geoMock.Setup(g => g.IsDatabaseAvailable).Returns(true);

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());

        // Act
        await viewModel.RefreshAsync();

        // Assert
        Assert.NotNull(viewModel.Stats);
        Assert.Equal(500, viewModel.Stats.TotalAlerts);
        Assert.Equal(342, viewModel.Stats.UniqueSources);
        Assert.NotEmpty(viewModel.RecentDecisions);
        Assert.True(viewModel.GeoOk);
    }

    [Fact]
    public async Task Refresh_HandlesApiExceptionGracefully()
    {
        // Arrange
        _apiMock.Setup(a => a.GetSnapshotStatsAsync()).ThrowsAsync(new Exception("API unavailable"));

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () => await viewModel.RefreshAsync());
    }

    [Fact]
    public async Task HubUpdateAndDeleteDecision_SetActionState()
    {
        // Arrange
        var hubUpdateResult = new CrowSecResult(true, 1.5f);
        _cliMock.Setup(c => c.HubUpdateAsync()).ReturnsAsync(hubUpdateResult);

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());

        // Act
        await viewModel.HubUpdateAsync();

        // Assert
        Assert.False(viewModel.IsHubBusy);
    }

    [Fact]
    public async Task DeleteDecision_FailsAndSetsError()
    {
        // Arrange
        var failResult = new CrowSecResult(false, 0.3f, "Permission denied");
        _cliMock.Setup(c => c.DeleteDecisionAsync(It.IsAny<long>())).ReturnsAsync(failResult);

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());
        viewModel.RecentDecisions = new List<CsDecision> { new CsDecision { Id = 100 } };

        // Act
        await viewModel.DeleteDecisionAsync(100);

        // Assert
        Assert.True(viewModel.ActionError);
        Assert.Contains("Permission denied", viewModel.ActionMessage);
    }

    [Fact]
    public async Task BackupConfigAsync_Succeeds()
    {
        // Arrange
        _configMock.Setup(c => c.BackupAsync("/var/lib/crowdsec-manager/backups"))
            .Returns(Task.CompletedTask);

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());

        // Act
        await viewModel.BackupConfigAsync();

        // Assert
        Assert.False(viewModel.ActionError);
        Assert.Contains("/var/lib/crowdsec-manager/backups", viewModel.ActionMessage);
    }

    [Fact]
    public async Task HubUpgrade_ExecutesSuccessfully()
    {
        // Arrange
        var upgradeResult = new CrowSecResult(true, 3.2f);
        _cliMock.Setup(c => c.HubUpgradeAsync()).ReturnsAsync(upgradeResult);

        var viewModel = new DashboardViewModel(_apiMock.Object, _cliMock.Object, _configMock.Object, _geoMock.Object, Mock.Of<ILogger<DashboardViewModel>>());

        // Act
        await viewModel.HubUpgradeAsync();

        // Assert
        Assert.False(viewModel.IsHubBusy);
    }
}

public class ThreatMapVisualViewModelTests
{
    private readonly Mock<IJSRuntime> _jsMock = new();
    private readonly Mock<IGeoIpService> _geoMock = new();
    private readonly Mock<IThreatMapEventBus> _eventsMock = new();

    [Fact]
    public async Task OnAfterRenderAsync_StartsMapOnFirstRender()
    {
        // Arrange
        var viewModel = new ThreatMapViewModel(_jsMock.Object, _geoMock.Object, _eventsMock.Object, Mock.Of<ILogger<ThreatMapViewModel>>());
        var eventsSnapshot = new ThreatMapEventBusSnapshot(new ThreatMapStats(), [], new List<string>());

        _geoMock.Setup(g => g.Home).Returns(new GeoLocation { Latitude = 48.8566, Longitude = 2.3522, City = "Paris" });
        _geoMock.Setup(g => g.IsDatabaseAvailable).Returns(true);
        _eventsMock.Setup(e => e.GetSnapshot()).Returns(eventsSnapshot);

        // Act
        await viewModel.OnAfterRenderAsync(firstRender: true);

        // Assert
        Assert.True(viewModel._started);
    }

    [Fact]
    public async Task OnAfterRenderAsync_SkipsStartOnSubsequentRenders()
    {
        // Arrange
        var viewModel = new ThreatMapViewModel(_jsMock.Object, _geoMock.Object, _eventsMock.Object, Mock.Of<ILogger<ThreatMapViewModel>>());

        // Act
        await viewModel.OnAfterRenderAsync(firstRender: false);

        // Assert
        Assert.False(viewModel._started);
    }

    [Fact]
    public async Task PlotThreat_AddsToRecentList()
    {
        // Arrange
        var jsInvokeTask = new Task(() => { });
        _jsMock.Setup(j => j.InvokeVoidAsync("threatMap.plotThreat", It.IsAny<ThreatEvent>()))
            .Returns(jsInvokeTask);

        var threatEvent = new ThreatEvent 
        { 
            SourceIp = "203.0.113.5", 
            Source = new CsSource { Country = "FR", Latitude = 48.8, Longitude = 2.3 },
            Severity = "High",
            AlertId = 123,
            Timestamp = DateTime.UtcNow
        };

        var viewModel = new ThreatMapViewModel(_jsMock.Object, _geoMock.Object, _eventsMock.Object, Mock.Of<ILogger<ThreatMapViewModel>>());
        viewModel.Recent = new List<ThreatEvent>();

        // Act
        await viewModel.ProcessThreatAsync(threatEvent, new ThreatMapStats());

        // Assert
        Assert.Contains(threatEvent, viewModel.Recent);
    }

    [Fact]
    public async Task Dispose_CallsJsDisposeAndCleansSubscriptions()
    {
        // Arrange
        var jsDisposeTask = new Task(() => { });
        _jsMock.Setup(j => j.InvokeVoidAsync("threatMap.dispose")).Returns(jsDisposeTask);

        var viewModel = new ThreatMapViewModel(_jsMock.Object, _geoMock.Object, _eventsMock.Object, Mock.Of<ILogger<ThreatMapViewModel>>());
        viewModel._subscribed = true;

        // Act & Assert
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task StartMap_UpdatesStatsAndReplayBuffer()
    {
        // Arrange
        var jsInvokeTask = new Task(() => { });
        _jsMock.Setup(j => j.InvokeVoidAsync("threatMap.init", It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(jsInvokeTask);
        
        var stats = new ThreatMapStats { TotalAlerts = 100 };
        _eventsMock.Setup(e => e.GetSnapshot()).Returns(new ThreatMapEventBusSnapshot(stats, []));

        var viewModel = new ThreatMapViewModel(_jsMock.Object, _geoMock.Object, _eventsMock.Object, Mock.Of<ILogger<ThreatMapViewModel>>());

        // Act
        await viewModel.StartMapAsync();

        // Assert
        Assert.Equal(stats, viewModel.Stats);
    }
}

public class AlertsDecisionsInteractionTests
{
    private readonly Mock<ICrowdSecCliService> _cliMock = new();

    [Fact]
    public async Task Refresh_LoadsAlertsFromCli()
    {
        // Arrange
        var alerts = new List<CsAlert>
        {
            new CsAlert { Id = 1, Scenario = "test-scenario", Source = new CsSource { Value = "192.168.1.100", Scope = "Ip" } },
            new CsAlert { Id = 2, Scenario = "http-probe", Source = new CsSource { Value = "10.0.0.5", Scope = "Ip" } }
        };
        _cliMock.Setup(c => c.ListAlertsAsync(100, null, null, null, null, null))
            .ReturnsAsync(alerts);

        var viewModel = new AlertsViewModel(_cliMock.Object, Mock.Of<ILogger<AlertsViewModel>>());

        // Act
        await viewModel.RefreshAsync();

        // Assert
        Assert.NotEmpty(viewModel.Alerts);
        Assert.Equal(2, viewModel.Alerts.Count);
    }

    [Fact]
    public async Task ClearFilters_ResetsAllFilterFields()
    {
        // Arrange
        var viewModel = new AlertsViewModel(_cliMock.Object, Mock.Of<ILogger<AlertsViewModel>>());
        viewModel.Ip = "192.168.1.1";
        viewModel.Scenario = "test";
        viewModel.Since = "2024-01-01";

        // Act
        await viewModel.ClearFiltersAsync();

        // Assert
        Assert.Equal("", viewModel.Ip);
        Assert.Equal("", viewModel.Scenario);
        Assert.Equal("", viewModel.Since);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAlertAndRefreshes()
    {
        // Arrange
        var deleteResult = new CrowSecResult(true, 0.1f);
        _cliMock.Setup(c => c.RunAsync("alerts", "delete", "--id", It.IsAny<string>()))
            .ReturnsAsync(deleteResult);

        var viewModel = new AlertsViewModel(_cliMock.Object, Mock.Of<ILogger<AlertsViewModel>>());
        viewModel.Alerts = new List<CsAlert> { new CsAlert { Id = 42 } };

        // Act
        await viewModel.DeleteAsync(42);

        // Assert
        Assert.DoesNotContain(It.Is<CsAlert>(a => a.Id == 42), viewModel.Alerts);
    }

    [Fact]
    public async Task BanAsync_CanBanIpSource()
    {
        // Arrange
        var alert = new CsAlert 
        { 
            Id = 1, 
            Scenario = null, 
            Source = new CsSource { Value = "192.168.1.100", Scope = "Ip" } 
        };

        var viewModel = new AlertsViewModel(_cliMock.Object, Mock.Of<ILogger<AlertsViewModel>>());

        // Act & Assert
        Assert.True(viewModel.CanBan(alert));
    }

    [Fact]
    public async Task BanAsync_AddsDecisionToCli()
    {
        // Arrange
        var addResult = new CrowSecResult(true, 1.2f);
        _cliMock.Setup(c => c.AddDecisionAsync(It.IsAny<string>(), "4h", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(addResult);

        var alert = new CsAlert { Id = 1, Scenario = null, Source = new CsSource { Value = "192.168.1.50", Scope = "Ip" } };
        var viewModel = new AlertsViewModel(_cliMock.Object, Mock.Of<ILogger<AlertsViewModel>>());

        // Act
        await viewModel.BanAsync(alert);

        // Assert
        Assert.False(viewModel.IsBanning);
    }
}
