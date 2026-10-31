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

    [Fact]
    public void HubParser_FlattensCategoryGroupsAndDerivesInstallState()
    {
        const string json = """
            {
              "collections": [
                { "name": "crowdsecurity/nginx", "local_version": "1.2", "local_path": "/etc/crowdsec/collections/nginx.yaml", "description": "nginx", "status": "enabled" },
                { "name": "crowdsecurity/apache", "local_version": "", "local_path": "", "description": "apache", "status": "disabled,update-available" },
                { "name": "crowdsecurity/custom", "local_version": "0.1", "local_path": "/etc/crowdsec/collections/custom.yaml", "description": "custom", "status": "enabled,tainted,update-available" }
              ],
              "appsec-rules": [
                { "name": "crowdsecurity/base", "local_version": "", "local_path": "", "description": "base", "status": "disabled,update-available" }
              ]
            }
            """;

        var items = CrowdSecHubParser.Parse(json);

        Assert.Equal(4, items.Count);
        Assert.Collection(items,
            item => { Assert.Equal("collections", item.Type); Assert.True(item.Installed); Assert.True(item.UpToDate); },
            item => { Assert.Equal("collections", item.Type); Assert.False(item.Installed); Assert.False(item.UpToDate); },
            item => { Assert.Equal("collections", item.Type); Assert.True(item.Installed); Assert.True(item.Tainted); Assert.False(item.UpToDate); },
            item => { Assert.Equal("appsec-rules", item.Type); Assert.False(item.Installed); });
    }
}
