using CrowdSecManager.Services;
using Xunit;

namespace CrowdSecManager.Tests;

/// <summary>Tests for the additional cscli commands added in this session:
/// machines register/delete/prune, bouncers inspect/prune, hub search/branch/types,
/// config show/show-yaml/backup/restore/feature-flags, lapi status/register/context,
/// explain log-line/log-file, and filtered alerts/decisions list.</summary>
public sealed class CrowdSecCommandBuilderMoreTests
{
    // ---- Machines ----

    [Fact]
    public void MachinesList_UsesMachinesListSubcommand()
    {
        Assert.Equal(new[] { "machines", "list" }, CrowdSecCommandBuilder.MachinesList());
    }

    [Fact]
    public void MachinesRegister_WithoutName_GeneratesBareRegister()
    {
        Assert.Equal(new[] { "machines", "register" }, CrowdSecCommandBuilder.MachinesRegister());
    }

    [Fact]
    public void MachinesRegister_WithName_AppendsMachineFlag()
    {
        Assert.Equal(
            new[] { "machines", "register", "--machine", "myhost" },
            CrowdSecCommandBuilder.MachinesRegister("myhost"));
    }

    [Fact]
    public void MachinesRegister_WithAutoValidate_AppendsAutoFlag()
    {
        Assert.Equal(
            new[] { "machines", "register", "--machine", "myhost", "--auto" },
            CrowdSecCommandBuilder.MachinesRegister("myhost", autoValidate: true));
    }

    [Fact]
    public void MachinesDelete_PassesMachineNameAsPositional()
    {
        Assert.Equal(new[] { "machines", "delete", "myhost" }, CrowdSecCommandBuilder.MachinesDelete("myhost"));
    }

    [Fact]
    public void MachinesPrune_HasNoExtraArgs()
    {
        Assert.Equal(new[] { "machines", "prune" }, CrowdSecCommandBuilder.MachinesPrune());
    }

    // ---- Bouncers ----

    [Fact]
    public void BouncersInspect_PassesNameAsPositional()
    {
        Assert.Equal(new[] { "bouncers", "inspect", "nginx-bouncer" },
            CrowdSecCommandBuilder.BouncersInspect("nginx-bouncer"));
    }

    [Fact]
    public void BouncersPrune_HasNoExtraArgs()
    {
        Assert.Equal(new[] { "bouncers", "prune" }, CrowdSecCommandBuilder.BouncersPrune());
    }

    [Fact]
    public void BouncersAdd_WithoutKey_GeneratesBareAddCommand()
    {
        // cscli bouncers add NAME (no --key) -> cscli generates the key, printed to stdout
        Assert.Equal(
            new[] { "bouncers", "add", "nginx-bouncer" },
            CrowdSecCommandBuilder.BouncersAdd("nginx-bouncer"));
    }

    [Fact]
    public void BouncersAdd_WithKey_AppendsKeyFlag()
    {
        // cscli bouncers add NAME --key KEY -> cscli uses the provided key verbatim
        Assert.Equal(
            new[] { "bouncers", "add", "nginx-bouncer", "--key", "mysecretkey123" },
            CrowdSecCommandBuilder.BouncersAdd("nginx-bouncer", "mysecretkey123"));
    }

    [Fact]
    public void BouncersAdd_WithEmptyKey_TreatsAsNoKey()
    {
        // Empty/whitespace key should not append --key (cscli would reject an empty value)
        Assert.Equal(
            new[] { "bouncers", "add", "nginx-bouncer" },
            CrowdSecCommandBuilder.BouncersAdd("nginx-bouncer", ""));
        Assert.Equal(
            new[] { "bouncers", "add", "nginx-bouncer" },
            CrowdSecCommandBuilder.BouncersAdd("nginx-bouncer", "   "));
        Assert.Equal(
            new[] { "bouncers", "add", "nginx-bouncer" },
            CrowdSecCommandBuilder.BouncersAdd("nginx-bouncer", null));
    }

    // ---- Hub extras ----

    [Fact]
    public void HubSearch_PassesQueryAsPositional()
    {
        Assert.Equal(new[] { "hub", "search", "nginx" }, CrowdSecCommandBuilder.HubSearch("nginx"));
    }

    [Fact]
    public void HubBranch_And_Types_HaveNoExtraArgs()
    {
        Assert.Equal(new[] { "hub", "branch" }, CrowdSecCommandBuilder.HubBranch());
        Assert.Equal(new[] { "hub", "types" }, CrowdSecCommandBuilder.HubTypes());
    }

    // ---- Config ----

    [Fact]
    public void ConfigShow_And_ShowYaml_HaveNoExtraArgs()
    {
        Assert.Equal(new[] { "config", "show" }, CrowdSecCommandBuilder.ConfigShow());
        Assert.Equal(new[] { "config", "show-yaml" }, CrowdSecCommandBuilder.ConfigShowYaml());
    }

    [Fact]
    public void ConfigBackup_PassesTargetFileAsPositional()
    {
        Assert.Equal(
            new[] { "config", "backup", "/tmp/cs.tgz" },
            CrowdSecCommandBuilder.ConfigBackup("/tmp/cs.tgz"));
    }

    [Fact]
    public void ConfigRestore_PassesSourceFileAsPositional()
    {
        Assert.Equal(
            new[] { "config", "restore", "/tmp/cs.tgz" },
            CrowdSecCommandBuilder.ConfigRestore("/tmp/cs.tgz"));
    }

    [Fact]
    public void ConfigFeatureFlags_HasNoExtraArgs()
    {
        Assert.Equal(new[] { "config", "feature-flags" }, CrowdSecCommandBuilder.ConfigFeatureFlags());
    }

    // ---- LAPI ----

    [Fact]
    public void LapiStatus_HasNoExtraArgs()
    {
        Assert.Equal(new[] { "lapi", "status" }, CrowdSecCommandBuilder.LapiStatus());
    }

    [Fact]
    public void LapiRegister_WithAllFlags_AppendsInOrder()
    {
        var args = CrowdSecCommandBuilder.LapiRegister("myhost", "http://1.2.3.4:8080", "tok123");
        Assert.Equal(
            new[] { "lapi", "register", "--machine", "myhost", "--url", "http://1.2.3.4:8080", "--token", "tok123" },
            args);
    }

    [Fact]
    public void LapiRegister_WithOnlyMachine_OnlyAddsMachineFlag()
    {
        Assert.Equal(
            new[] { "lapi", "register", "--machine", "myhost" },
            CrowdSecCommandBuilder.LapiRegister("myhost", null, null));
    }

    [Fact]
    public void LapiRegister_WithNothing_GeneratesBareRegister()
    {
        Assert.Equal(new[] { "lapi", "register" }, CrowdSecCommandBuilder.LapiRegister(null, null, null));
    }

    [Fact]
    public void LapiContextList_And_Show_HaveCorrectArgs()
    {
        Assert.Equal(new[] { "lapi", "context", "list" }, CrowdSecCommandBuilder.LapiContextList());
        Assert.Equal(new[] { "lapi", "context", "show", "prod" }, CrowdSecCommandBuilder.LapiContextShow("prod"));
    }

    // ---- Explain ----

    [Fact]
    public void ExplainLogLine_UsesLogAndTypeFlags()
    {
        var args = CrowdSecCommandBuilder.ExplainLogLine("hello world", "nginx");
        Assert.Equal(new[] { "explain", "--log", "hello world", "--type", "nginx" }, args);
    }

    [Fact]
    public void ExplainLogLine_WithLabels_AppendsLabelsFlag()
    {
        var args = CrowdSecCommandBuilder.ExplainLogLine("hello", "syslog", "env:prod");
        Assert.Equal(new[] { "explain", "--log", "hello", "--type", "syslog", "--labels", "env:prod" }, args);
    }

    [Fact]
    public void ExplainLogFile_UsesFileAndTypeFlags()
    {
        var args = CrowdSecCommandBuilder.ExplainLogFile("/var/log/x.log", "nginx");
        Assert.Equal(new[] { "explain", "--file", "/var/log/x.log", "--type", "nginx" }, args);
    }

    [Fact]
    public void ExplainLogFile_WithVerbose_AppendsVerboseFlag()
    {
        var args = CrowdSecCommandBuilder.ExplainLogFile("/var/log/x.log", "nginx", verbose: true);
        Assert.Equal(new[] { "explain", "--file", "/var/log/x.log", "--type", "nginx", "--verbose" }, args);
    }

    // ---- Alerts/Decisions with filters ----

    [Fact]
    public void AlertsList_WithNoFilters_HasLimit()
    {
        Assert.Equal(new[] { "alerts", "list", "-l", "100" },
            CrowdSecCommandBuilder.AlertsList(100));
    }

    [Fact]
    public void AlertsList_WithAllFilters_AppendsAllFlags()
    {
        var args = CrowdSecCommandBuilder.AlertsList(50, "1.2.3.4", "crowdsecurity/ssh-bf", "4h", "1h", "ban");
        Assert.Equal(new[]
        {
            "alerts", "list", "-l", "50",
            "--ip", "1.2.3.4",
            "--scenario", "crowdsecurity/ssh-bf",
            "--since", "4h",
            "--until", "1h",
            "--type", "ban"
        }, args);
    }

    [Fact]
    public void DecisionsList_WithNoFilters_HasBareCommand()
    {
        Assert.Equal(new[] { "decisions", "list" }, CrowdSecCommandBuilder.DecisionsList());
    }

    [Fact]
    public void DecisionsList_WithAllFilters_AppendsAllFlags()
    {
        var args = CrowdSecCommandBuilder.DecisionsList("1.2.3.4", "crowdsecurity/ssh-bf", "4h", "1h", "ban");
        Assert.Equal(new[]
        {
            "decisions", "list",
            "--ip", "1.2.3.4",
            "--scenario", "crowdsecurity/ssh-bf",
            "--since", "4h",
            "--until", "1h",
            "--type", "ban"
        }, args);
    }
}
