using CrowdSecManager.Services;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CrowdSecCommandBuilderTests
{
    [Fact]
    public void AddDecision_UsesIpFlagAndPreservesReasonAsOneArgument()
    {
        var args = CrowdSecCommandBuilder.AddDecision("203.0.113.5", "2h", "manual review", "ban");

        Assert.Equal(new[]
        {
            "decisions", "add", "--ip", "203.0.113.5", "--duration", "2h",
            "--type", "ban", "--reason", "manual review"
        }, args);
    }

    [Fact]
    public void AddDecision_UsesRangeFlagForCidr()
    {
        var args = CrowdSecCommandBuilder.AddDecision("203.0.113.0/24", "4h", null, "ban");

        Assert.Equal(new[]
        {
            "decisions", "add", "--range", "203.0.113.0/24", "--duration", "4h", "--type", "ban"
        }, args);
    }

    [Fact]
    public void DeleteDecision_UsesDecisionIdFlag()
    {
        var args = CrowdSecCommandBuilder.DeleteDecision(id: 42);

        Assert.Equal(new[] { "decisions", "delete", "--id", "42" }, args);
    }

    [Fact]
    public void DeleteAllDecisions_UsesAllFlag()
    {
        var args = CrowdSecCommandBuilder.DeleteDecision(all: true);

        Assert.Equal(new[] { "decisions", "delete", "--all" }, args);
    }

    [Fact]
    public void UpgradeItem_UsesTypedUpgradeCommand()
    {
        var args = CrowdSecCommandBuilder.UpgradeItem("Collection", "crowdsecurity/nginx");

        Assert.Equal(new[] { "collections", "upgrade", "crowdsecurity/nginx" }, args);
    }

    [Fact]
    public void ReloadSignalTargetsOnlyTheCrowdSecProcess()
    {
        var args = CrowdSecCommandBuilder.ReloadSignal();

        Assert.Equal(new[] { "--signal", "HUP", "--exact", "crowdsec" }, args);
    }

    [Fact]
    public void TestConfigTargetsTheConfiguredMainYaml()
    {
        var args = CrowdSecCommandBuilder.TestConfig("/etc/crowdsec");

        Assert.Equal(new[] { "-t", "-c", Path.Combine("/etc/crowdsec", "config.yaml") }, args);
    }
}
