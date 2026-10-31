using CrowdSecManager.Services;
using Xunit;

namespace CrowdSecManager.Tests;

/// <summary>Tests for the new simulation / notifications / console / allowlists commands
/// added to <see cref="CrowdSecCommandBuilder"/>, plus the bypass-allowlist flag on
/// decision add.</summary>
public sealed class CrowdSecCommandBuilderExtendedTests
{
    // ---- AddDecision with --bypass-allowlist ----

    [Fact]
    public void AddDecision_WithoutBypass_PreservesExistingBehavior()
    {
        // Default parameter must keep producing the same args as before
        // (backwards compatibility with the existing UI flow and tests).
        var args = CrowdSecCommandBuilder.AddDecision("203.0.113.5", "2h", "manual", "ban");

        Assert.Equal(new[]
        {
            "decisions", "add", "--ip", "203.0.113.5",
            "--duration", "2h", "--type", "ban", "--reason", "manual"
        }, args);
    }

    [Fact]
    public void AddDecision_WithBypass_AppendsBypassAllowlistFlagBeforeReason()
    {
        var args = CrowdSecCommandBuilder.AddDecision(
            "203.0.113.5", "2h", "manual", "ban", bypassAllowlist: true);

        Assert.Equal(new[]
        {
            "decisions", "add", "--ip", "203.0.113.5",
            "--duration", "2h", "--type", "ban",
            "--bypass-allowlist",
            "--reason", "manual"
        }, args);
    }

    [Fact]
    public void AddDecision_WithBypass_AndNoReason_OnlyAddsBypassFlag()
    {
        var args = CrowdSecCommandBuilder.AddDecision(
            "203.0.113.0/24", "4h", null, "ban", bypassAllowlist: true);

        Assert.Equal(new[]
        {
            "decisions", "add", "--range", "203.0.113.0/24",
            "--duration", "4h", "--type", "ban",
            "--bypass-allowlist"
        }, args);
    }

    // ---- Simulation ----

    [Fact]
    public void SimulationList_UsesSimulationSubcommand()
    {
        Assert.Equal(new[] { "simulation", "list" }, CrowdSecCommandBuilder.SimulationList());
    }

    [Fact]
    public void SimulationStatus_UsesStatusSubcommand()
    {
        Assert.Equal(new[] { "simulation", "status" }, CrowdSecCommandBuilder.SimulationStatus());
    }

    [Fact]
    public void SimulationEnable_DisabledCommandsHaveNoExtraArgs()
    {
        Assert.Equal(new[] { "simulation", "enable" }, CrowdSecCommandBuilder.SimulationEnable());
        Assert.Equal(new[] { "simulation", "disable" }, CrowdSecCommandBuilder.SimulationDisable());
    }

    [Theory]
    [InlineData("crowdsecurity/ssh-bf")]
    [InlineData("crowdsecurity/nginx-logs")]
    public void SimulationAdd_Remove_PassScenarioAsPositional(string scenario)
    {
        Assert.Equal(new[] { "simulation", "add", scenario }, CrowdSecCommandBuilder.SimulationAdd(scenario));
        Assert.Equal(new[] { "simulation", "remove", scenario }, CrowdSecCommandBuilder.SimulationRemove(scenario));
    }

    // ---- Notifications ----

    [Fact]
    public void NotificationsList_UsesNotificationsSubcommand()
    {
        Assert.Equal(new[] { "notifications", "list" }, CrowdSecCommandBuilder.NotificationsList());
    }

    [Fact]
    public void NotificationsInspect_PassesPluginName()
    {
        Assert.Equal(
            new[] { "notifications", "inspect", "email" },
            CrowdSecCommandBuilder.NotificationsInspect("email"));
    }

    // ---- Console (Central API) ----

    [Fact]
    public void ConsoleStatus_UsesStatusSubcommand()
    {
        Assert.Equal(new[] { "console", "status" }, CrowdSecCommandBuilder.ConsoleStatus());
    }

    [Fact]
    public void ConsoleEnable_Disabled_PassAlertsArgument()
    {
        Assert.Equal(new[] { "console", "enable", "alerts" }, CrowdSecCommandBuilder.ConsoleEnable());
        Assert.Equal(new[] { "console", "disable", "alerts" }, CrowdSecCommandBuilder.ConsoleDisable());
    }

    // ---- Allowlists ----

    [Fact]
    public void AllowlistsList_UsesAllowlistsSubcommand()
    {
        Assert.Equal(new[] { "allowlists", "list" }, CrowdSecCommandBuilder.AllowlistsList());
    }
}
