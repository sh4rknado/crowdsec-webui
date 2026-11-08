using CrowdsecManagerModel;
using CrowdSecManager.Services;
using CrowdSecManager.ViewModels;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrowdSecManager.ViewModels.Tests;

public sealed class AlertsViewModelTests
{
    [Fact]
    public async Task Refresh_ClampsLimitAndTrimsFilters()
    {
        object?[]? received = null;
        var alert = new CsAlert { Id = 7 };
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) =>
        {
            if (name == "ListAlertsAsync")
            {
                received = args;
                return Task.FromResult<IReadOnlyList<CsAlert>>([alert]);
            }
            throw new InvalidOperationException(name);
        });
        var vm = new AlertsViewModel(cli, NullLogger<AlertsViewModel>.Instance)
        {
            Limit = 5000, Ip = " 1.2.3.4 ", Scenario = " ", Since = " 1h ", Until = "", Type = " ban "
        };

        await vm.RefreshAsync();

        Assert.Equal(1000, received![0]);
        Assert.Equal("1.2.3.4", received[1]);
        Assert.Null(received[2]);
        Assert.Equal("1h", received[3]);
        Assert.Null(received[4]);
        Assert.Equal("ban", received[5]);
        Assert.Same(alert, Assert.Single(vm.Alerts));
    }

    [Fact]
    public async Task ClearFilters_ResetsAllAndRefreshes()
    {
        object?[]? received = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) =>
        {
            if (name == "ListAlertsAsync") { received = args; return Task.FromResult<IReadOnlyList<CsAlert>>([]); }
            throw new InvalidOperationException(name);
        });
        var vm = new AlertsViewModel(cli, NullLogger<AlertsViewModel>.Instance)
        {
            Ip = "ip", Scenario = "scenario", Since = "since", Until = "until", Type = "type"
        };

        await vm.ClearFiltersAsync();

        Assert.Equal(100, received![0]);
        Assert.All(received.Skip(1).Take(5), Assert.Null);
        Assert.Equal(default(CancellationToken), received[6]);
        Assert.Equal("", vm.Ip);
        Assert.Equal("", vm.Scenario);
        Assert.Equal("", vm.Since);
        Assert.Equal("", vm.Until);
        Assert.Equal("", vm.Type);
    }

    [Fact]
    public async Task Delete_UsesInvariantIdAndRefreshes()
    {
        string[]? command = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "RunAsync" => CaptureCommand(args, value => command = value),
            "ListAlertsAsync" => Task.FromResult<IReadOnlyList<CsAlert>>([]),
            _ => throw new InvalidOperationException(name)
        });
        await new AlertsViewModel(cli, NullLogger<AlertsViewModel>.Instance).DeleteAsync(123);
        Assert.Equal(["alerts", "delete", "--id", "123"], command!);
    }

    [Theory]
    [InlineData("abcdef", 3, "abc...")]
    [InlineData("abc", 3, "abc")]
    [InlineData(null, 3, null)]
    public void Truncate_ReturnsExpectedValue(string? value, int length, string? expected)
        => Assert.Equal(expected, AlertsViewModel.Truncate(value, length));

    [Fact]
    public async Task Initialize_RefreshesAlerts()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ListAlertsAsync"
            ? Task.FromResult<IReadOnlyList<CsAlert>>([new CsAlert { Id = 5 }])
            : throw new InvalidOperationException(name));
        var vm = new AlertsViewModel(cli, NullLogger<AlertsViewModel>.Instance);

        await vm.InitializeAsync();

        Assert.Equal(5, Assert.Single(vm.Alerts).Id);
    }

    private static object CaptureCommand(object?[]? args, Action<string[]> capture)
    {
        capture((string[])args![0]!);
        return Task.FromResult(new CliResult());
    }
}

public sealed class BouncersViewModelTests
{
    [Fact]
    public async Task Add_RequiresNameWithoutCallingService()
    {
        var vm = new BouncersViewModel(ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()), NullLogger<BouncersViewModel>.Instance);
        await vm.AddAsync();
        Assert.True(vm.HasError);
        Assert.Equal("Name is required.", vm.Message);
        Assert.False(vm.IsAdding);
    }

    [Fact]
    public async Task Add_ExtractsGeneratedKeyAndRefreshes()
    {
        var calls = new List<string>();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) =>
        {
            calls.Add(name);
            return name switch
            {
                "AddBouncerAsync" => Task.FromResult(new CliResult { Stdout = "Api key:\n  abcDEF/123=" }),
                "ListBouncersAsync" => Task.FromResult<IReadOnlyList<CsBouncer>>([]),
                _ => throw new InvalidOperationException(name)
            };
        });
        var vm = new BouncersViewModel(cli, NullLogger<BouncersViewModel>.Instance) { NewName = "proxy" };

        await vm.AddAsync();

        Assert.Equal("abcDEF/123=", vm.ApiKey);
        Assert.Empty(vm.Bouncers);
        Assert.Equal("", vm.NewName);
        Assert.False(vm.HasError);
        Assert.False(vm.IsAdding);
        Assert.Equal(["AddBouncerAsync", "ListBouncersAsync"], calls);
    }

    [Fact]
    public async Task Inspect_FormatsFailedResultAndClearsBusyState()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "InspectBouncerAsync"
            ? Task.FromResult(new CliResult { ExitCode = 4, Stderr = "not found" })
            : throw new InvalidOperationException(name));
        var vm = new BouncersViewModel(cli, NullLogger<BouncersViewModel>.Instance);
        await vm.InspectAsync("missing");
        Assert.Equal("missing", vm.InspectName);
        Assert.Contains("Exit code 4", vm.InspectOutput);
        Assert.Contains("not found", vm.InspectOutput);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Reload_RestoresDraftFromSelectedFileAndClearsError()
    {
        var content = "from disk: first";
        var config = ServiceProxy.Create<IConfigFileService>((name, args) => name switch
        {
            "Load" => new ConfigFileInfo { RelativePath = (string)args![0]!, Content = content },
            _ => throw new InvalidOperationException(name)
        });
        var vm = new ConfigEditorViewModel(config, ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()));
        await vm.SelectAsync("config.yaml");
        vm.Draft = "unsaved";
        content = "from disk: second";
        await vm.ReloadAsync();

        Assert.Equal(content, vm.Draft);
        Assert.Equal("Reloaded from disk.", vm.Message);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task DeleteAndPrune_ReportResultsAndRefresh()
    {
        var calls = new List<string>();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) =>
        {
            calls.Add(name);
            return name switch
            {
                "DeleteBouncerAsync" => Task.FromResult(new CliResult { ExitCode = 1, Stderr = "denied" }),
                "PruneBouncersAsync" => Task.FromResult(new CliResult()),
                "ListBouncersAsync" => Task.FromResult<IReadOnlyList<CsBouncer>>([]),
                _ => throw new InvalidOperationException(name)
            };
        });
        var vm = new BouncersViewModel(cli, NullLogger<BouncersViewModel>.Instance);

        await vm.InitializeAsync();
        await vm.DeleteAsync("proxy");
        Assert.True(vm.HasError);
        Assert.Contains("denied", vm.Message);
        await vm.PruneAsync();
        Assert.False(vm.HasError);
        Assert.Equal("Pruned revoked bouncers.", vm.Message);
        Assert.False(vm.IsBusy);
        Assert.Equal(["ListBouncersAsync", "DeleteBouncerAsync", "ListBouncersAsync", "PruneBouncersAsync", "ListBouncersAsync"], calls);
    }
}

public sealed class ConfigEditorViewModelTests
{
    [Fact]
    public async Task InitializeSelectValidateAndSave_UsesSelectedFile()
    {
        var file = new ConfigFileInfo { RelativePath = "config.yaml", Content = "key: value" };
        string? savedPath = null;
        string? savedContent = null;
        var config = ServiceProxy.Create<IConfigFileService>((name, args) => name switch
        {
            "ListConfigFiles" => new[] { file },
            "Load" => file,
            "ValidateYaml" => (true, (string?)null),
            "Save" => Save(args, value => { savedPath = (string)args![0]!; savedContent = value; }),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new ConfigEditorViewModel(config, ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()));

        await vm.InitializeAsync();
        await vm.SelectAsync("config.yaml");
        vm.Draft = "updated: true";
        await vm.ValidateAsync();
        vm.Save();

        Assert.Single(vm.Files);
        Assert.Equal("config.yaml", vm.Selected);
        Assert.False(vm.HasError);
        Assert.Equal("config.yaml", savedPath);
        Assert.Equal("updated: true", savedContent);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CreateNewFile_TrimsPathAndSelectsCreatedFile()
    {
        var createdFile = new ConfigFileInfo { RelativePath = "profiles/new.yaml", Content = "" };
        string? createdPath = null;
        var config = ServiceProxy.Create<IConfigFileService>((name, args) => name switch
        {
            "TryCreate" => Create(args, path => createdPath = path),
            "ListConfigFiles" => new[] { createdFile },
            "Load" => createdFile,
            _ => throw new InvalidOperationException(name)
        });
        var vm = new ConfigEditorViewModel(config, ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()))
        {
            NewFilePath = " /profiles/new.yaml "
        };

        await vm.CreateNewFileAsync();

        Assert.Equal("profiles/new.yaml", createdPath);
        Assert.Equal(createdFile.RelativePath, vm.Selected);
        Assert.Equal("", vm.NewFilePath);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ItemAndHubActions_RefreshAndFormatResults()
    {
        var calls = new List<string>();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) =>
        {
            calls.Add(name);
            return name switch
            {
                "ListHubAsync" => Task.FromResult<IReadOnlyList<CsHubItem>>([]),
                "HubUpdateAsync" or "HubUpgradeAsync" => Task.FromResult(new CliResult { Duration = TimeSpan.FromSeconds(2) }),
                "RemoveItemAsync" or "UpgradeItemAsync" => Task.FromResult(new CliResult()),
                "ReloadServiceAsync" => Task.FromResult(new CliResult { ExitCode = 1, Stderr = "offline" }),
                "HubBranchAsync" => Task.FromResult(new CliResult { Stdout = "branch" }),
                "HubTypesAsync" => Task.FromResult(new CliResult { Stdout = "types" }),
                _ => throw new InvalidOperationException(name)
            };
        });
        var vm = new HubManagementViewModel(cli, NullLogger<HubManagementViewModel>.Instance);
        var item = new CsHubItem { Name = "test", Type = "scenarios" };

        await vm.UpdateAsync();
        Assert.False(vm.IsBusy);
        await vm.UpgradeAllAsync();
        await vm.RemoveAsync(item);
        await vm.UpgradeAsync(item);
        await vm.ReloadCrowdSecAsync();
        Assert.True(vm.HasError);
        Assert.Contains("offline", vm.Message);
        await vm.HubBranchAsync();
        Assert.Equal("cscli hub branch", vm.DiagnosticTitle);
        Assert.Contains("branch", vm.DiagnosticOutput);
        await vm.HubTypesAsync();
        Assert.Equal("cscli hub types", vm.DiagnosticTitle);
        Assert.Contains("types", vm.DiagnosticOutput);
        Assert.False(vm.IsBusyDiag);
        Assert.Equal(11, calls.Count);
        Assert.Equal("abc...", HubManagementViewModel.Truncate("abcdef", 3));
    }

    [Fact]
    public void StaticFormattingHelpers_HandleBoundaries()
    {
        Assert.Equal("—", ConfigEditorViewModel.FormatSize(0));
        Assert.Equal("512 B", ConfigEditorViewModel.FormatSize(512));
        Assert.Equal((1536 / 1024.0).ToString("F1", CultureInfo.CurrentCulture) + " KiB", ConfigEditorViewModel.FormatSize(1536));
        Assert.Equal((1572864 / 1024.0 / 1024).ToString("F1", CultureInfo.CurrentCulture) + " MiB", ConfigEditorViewModel.FormatSize(1572864));
        Assert.Equal("—", ConfigEditorViewModel.FormatDate(DateTime.MinValue));
        Assert.True(ConfigEditorViewModel.IsHubManaged("Scenarios/test.yaml"));
        Assert.False(ConfigEditorViewModel.IsHubManaged("profiles.yaml"));
    }

    [Fact]
    public async Task ReloadAndValidate_HandleMissingAndInvalidFiles()
    {
        var config = ServiceProxy.Create<IConfigFileService>((name, _) => name switch
        {
            "Load" => null,
            "ValidateYaml" => (false, (string?)"invalid YAML"),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new ConfigEditorViewModel(config, ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()));

        await vm.SelectAsync("missing.yaml");
        await vm.ReloadAsync();
        await vm.ValidateAsync();

        Assert.Null(vm.Current);
        Assert.Equal("", vm.Draft);
        Assert.True(vm.HasError);
        Assert.Equal("invalid YAML", vm.Message);
        vm.Save();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CrowdSecCommandsAndBackup_UpdateTitlesOutputAndBusyFlags()
    {
        var calls = new List<string>();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) =>
        {
            calls.Add(name);
            return name switch
            {
                "ReloadServiceAsync" => Task.FromResult(new CliResult()),
                "ValidateEngineConfigAsync" => Task.FromResult(new CliResult { ExitCode = 2, Stderr = "invalid config" }),
                "ConfigShowAsync" => Task.FromResult(new CliResult { Stdout = "merged" }),
                "ConfigShowYamlAsync" => Task.FromResult(new CliResult { Stdout = "yaml" }),
                "ConfigFeatureFlagsAsync" => Task.FromResult(new CliResult { Stdout = "flags" }),
                "ConfigBackupAsync" => Task.FromResult(new CliResult { Stdout = "archive" }),
                _ => throw new InvalidOperationException(name)
            };
        });
        var vm = new ConfigEditorViewModel(ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()), cli);

        await vm.ApplyToCrowdSecAsync();
        Assert.False(vm.HasError);
        await vm.ValidateWithCrowdSecAsync();
        Assert.True(vm.HasError);
        Assert.Contains("invalid config", vm.Message);
        await vm.ShowMergedAsync();
        Assert.Equal("cscli config show", vm.ConfigOutputTitle);
        await vm.ShowMergedYamlAsync();
        Assert.Equal("cscli config show-yaml", vm.ConfigOutputTitle);
        await vm.ShowFeatureFlagsAsync();
        Assert.Equal("cscli config feature-flags", vm.ConfigOutputTitle);
        await vm.BackupConfigAsync();
        Assert.Equal("cscli config backup", vm.ConfigOutputTitle);
        Assert.Contains("archive", vm.ConfigOutput);
        Assert.False(vm.IsBusyConfig);
        Assert.Equal(["ReloadServiceAsync", "ValidateEngineConfigAsync", "ConfigShowAsync", "ConfigShowYamlAsync", "ConfigFeatureFlagsAsync", "ConfigBackupAsync"], calls);
    }

    [Fact]
    public void IsHubManaged_RecognizesEveryManagedDirectory()
    {
        foreach (var directory in new[] { "parsers", "scenarios", "postoverflows", "collections", "contexts", "appsec-rules", "appsec-configs" })
            Assert.True(ConfigEditorViewModel.IsHubManaged(directory + "/item.yaml"));
    }

    private static object Save(object?[]? args, Action<string> capture)
    {
        capture((string)args![1]!);
        return (true, (string?)null);
    }

    private static object Create(object?[]? args, Action<string> capture)
    {
        capture((string)args![0]!);
        args[1] = null;
        return (true, (string?)null);
    }
}

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task Refresh_LoadsStatsDecisionsAndGeoAvailability()
    {
        var stats = new ThreatMapStats { TotalAlerts = 9 };
        var decisions = new[] { new CsDecision { Id = 4 } };
        var api = ServiceProxy.Create<ICrowdSecApiService>((name, _) => name switch
        {
            "GetSnapshotStatsAsync" => Task.FromResult(stats),
            "GetDecisionsAsync" => Task.FromResult<IReadOnlyList<CsDecision>>(decisions),
            _ => throw new InvalidOperationException(name)
        });
        var geo = ServiceProxy.Create<IGeoIpService>((name, _) => name == "get_IsDatabaseAvailable" ? true : throw new InvalidOperationException(name));
        var vm = new DashboardViewModel(api, ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()),
            ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()), geo, NullLogger<DashboardViewModel>.Instance);

        await vm.InitializeAsync();

        Assert.Same(stats, vm.Stats);
        Assert.Same(decisions, vm.RecentDecisions);
        Assert.True(vm.GeoOk);
        Assert.True(vm.GeoDatabaseAvailable);
    }

    [Fact]
    public async Task BackupConfig_ReportsExceptionAndResetsBusyFlag()
    {
        var config = ServiceProxy.Create<IConfigFileService>((name, _) => name == "BackupAsync"
            ? Task.FromException(new IOException("disk full")) : throw new InvalidOperationException(name));
        var vm = new DashboardViewModel(ServiceProxy.Create<ICrowdSecApiService>((_, _) => throw new InvalidOperationException()),
            ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()), config,
            ServiceProxy.Create<IGeoIpService>((_, _) => false), NullLogger<DashboardViewModel>.Instance);

        await vm.BackupConfigAsync();

        Assert.True(vm.ActionError);
        Assert.Equal("disk full", vm.ActionMessage);
        Assert.False(vm.IsConfigBusy);
    }

    [Fact]
    public async Task HubUpdateAndDeleteDecision_SetActionState()
    {
        var refreshCount = 0;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name switch
        {
            "HubUpdateAsync" => Task.FromResult(new CliResult { Duration = TimeSpan.FromSeconds(1.2) }),
            "DeleteDecisionAsync" => Task.FromResult(new CliResult()),
            _ => throw new InvalidOperationException(name)
        });
        var api = ServiceProxy.Create<ICrowdSecApiService>((name, _) =>
        {
            if (name == "GetSnapshotStatsAsync") return Task.FromResult(new ThreatMapStats());
            if (name == "GetDecisionsAsync") { refreshCount++; return Task.FromResult<IReadOnlyList<CsDecision>>([]); }
            throw new InvalidOperationException(name);
        });
        var vm = new DashboardViewModel(api, cli, ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()),
            ServiceProxy.Create<IGeoIpService>((_, _) => false), NullLogger<DashboardViewModel>.Instance);

        await vm.HubUpdateAsync();
        Assert.False(vm.IsHubBusy);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, "Hub index updated in {0:F1}s.", 1.2), vm.ActionMessage);
        Assert.False(vm.ActionError);
        await vm.DeleteDecisionAsync(12);
        Assert.Equal(1, refreshCount);
    }
}

public sealed class DecisionsViewModelTests
{
    [Fact]
    public async Task Refresh_FallsBackToCliWhenApiFails()
    {
        var decision = new CsDecision { Id = 9 };
        var api = ServiceProxy.Create<ICrowdSecApiService>((name, _) => name == "GetDecisionsAsync"
            ? Task.FromException<IReadOnlyList<CsDecision>>(new IOException()) : throw new InvalidOperationException(name));
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ListDecisionsAsync"
            ? Task.FromResult<IReadOnlyList<CsDecision>>([decision]) : throw new InvalidOperationException(name));
        var vm = new DecisionsViewModel(api, cli, NullLogger<DecisionsViewModel>.Instance);

        await vm.InitializeAsync();

        Assert.Same(decision, Assert.Single(vm.Decisions));
    }

    [Fact]
    public async Task Add_RejectsMissingIpAndResetsBusyFlag()
    {
        var vm = new DecisionsViewModel(ServiceProxy.Create<ICrowdSecApiService>((_, _) => Task.FromResult<IReadOnlyList<CsDecision>>([])),
            ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()), NullLogger<DecisionsViewModel>.Instance);
        await vm.AddAsync();
        Assert.True(vm.HasError);
        Assert.Equal("IP address is required.", vm.Message);
        Assert.False(vm.IsAdding);
    }

    [Fact]
    public async Task Add_UsesInputsAndClearsSuccessfulDecision()
    {
        object?[]? addArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "AddDecisionAsync" => Capture(args, value => addArgs = value),
            "ListDecisionsAsync" => Task.FromResult<IReadOnlyList<CsDecision>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new DecisionsViewModel(ServiceProxy.Create<ICrowdSecApiService>((name, _) => name == "GetDecisionsAsync"
                ? Task.FromResult<IReadOnlyList<CsDecision>>([]) : throw new InvalidOperationException(name)), cli, NullLogger<DecisionsViewModel>.Instance)
        { Ip = "192.0.2.1", Duration = "2h", Type = "captcha", Reason = "test", BypassAllowlist = true };

        await vm.AddAsync();

        Assert.Equal("192.0.2.1", addArgs![0]);
        Assert.Equal("2h", addArgs[1]);
        Assert.Equal("test", addArgs[2]);
        Assert.Equal("captcha", addArgs[3]);
        Assert.Equal(true, addArgs[4]);
        Assert.Equal("", vm.Ip);
        Assert.False(vm.BypassAllowlist);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task DeleteAndDeleteAll_HandleFailureAndSuccess()
    {
        var deleteCalls = 0;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "DeleteDecisionAsync" => DeleteResult(args, ref deleteCalls),
            "ListDecisionsAsync" => Task.FromResult<IReadOnlyList<CsDecision>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var api = ServiceProxy.Create<ICrowdSecApiService>((name, _) => name == "GetDecisionsAsync"
            ? Task.FromResult<IReadOnlyList<CsDecision>>([]) : throw new InvalidOperationException(name));
        var vm = new DecisionsViewModel(api, cli, NullLogger<DecisionsViewModel>.Instance);

        await vm.DeleteAsync(8);
        Assert.True(vm.HasError);
        Assert.Contains("denied", vm.Message);
        await vm.DeleteAllAsync();
        Assert.False(vm.HasError);
        Assert.Equal("All decisions removed.", vm.Message);
        Assert.False(vm.IsDeletingAll);
        Assert.Equal(2, deleteCalls);
        Assert.Equal("abc...", DecisionsViewModel.Truncate("abcdef", 3));
    }

    private static Task<CliResult> DeleteResult(object?[]? args, ref int calls)
    {
        calls++;
        return Task.FromResult(calls == 1 ? new CliResult { ExitCode = 1, Stderr = "denied" } : new CliResult());
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult());
    }
}

public sealed class ExplainViewModelTests
{
    [Fact]
    public async Task ExplainLine_ValidatesAndTrimsLabels()
    {
        object?[]? callArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name == "ExplainLogLineAsync"
            ? Capture(args, value => callArgs = value) : throw new InvalidOperationException(name));
        var vm = new ExplainViewModel(cli, NullLogger<ExplainViewModel>.Instance) { LogLine = "event", Labels = " foo " };

        await vm.ExplainLineAsync();

        Assert.Equal("event", callArgs![0]);
        Assert.Equal("syslog", callArgs[1]);
        Assert.Equal("foo", callArgs[2]);
        Assert.Equal("explained", vm.Output);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExplainFile_PassesOptionsAndFormatsOutput()
    {
        object?[]? argsReceived = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name == "ExplainLogFileAsync"
            ? Capture(args, value => argsReceived = value)
            : throw new InvalidOperationException(name));
        var vm = new ExplainViewModel(cli, NullLogger<ExplainViewModel>.Instance)
        {
            FilePath = "/logs/access.log", Type = "nginx", Verbose = true
        };

        await vm.ExplainFileAsync();

        Assert.Equal("/logs/access.log", argsReceived![0]);
        Assert.Equal("nginx", argsReceived[1]);
        Assert.Equal(true, argsReceived[2]);
        Assert.Equal("explained", vm.Output);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ExplainFile_RejectsMissingPathAndClearResetsResult()
    {
        var vm = new ExplainViewModel(ServiceProxy.Create<ICrowdSecCliService>((_, _) => throw new InvalidOperationException()), NullLogger<ExplainViewModel>.Instance);
        await vm.ExplainFileAsync();
        Assert.True(vm.HasError);
        Assert.Equal("File path is required.", vm.Message);
        vm.ClearOutput();
        Assert.Null(vm.Output);
        Assert.Null(vm.Message);
        Assert.Equal(0, vm.ExitCode);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExplainLine_FormatsFailureOutput()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ExplainLogLineAsync"
            ? Task.FromResult(new CliResult { ExitCode = 2, Stderr = "invalid log" }) : throw new InvalidOperationException(name));
        var vm = new ExplainViewModel(cli, NullLogger<ExplainViewModel>.Instance) { LogLine = "line" };
        await vm.ExplainLineAsync();
        Assert.True(vm.HasError);
        Assert.Equal(2, vm.ExitCode);
        Assert.Contains("invalid log", vm.Output);
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult { Stdout = "explained" });
    }
}

public sealed class HubManagementViewModelTests
{
    [Fact]
    public async Task RefreshAndFiltered_SearchesNameTypeAndDescriptionCaseInsensitively()
    {
        var items = new[]
        {
            new CsHubItem { Name = "alpha", Type = "parsers", Description = "one" },
            new CsHubItem { Name = "beta", Type = "scenarios", Description = "two" }
        };
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ListHubAsync"
            ? Task.FromResult<IReadOnlyList<CsHubItem>>(items) : throw new InvalidOperationException(name));
        var vm = new HubManagementViewModel(cli, NullLogger<HubManagementViewModel>.Instance);
        await vm.InitializeAsync();
        vm.Filter = "SCEN";
        Assert.Same(items[1], Assert.Single(vm.Filtered));
        vm.Filter = "one";
        Assert.Same(items[0], Assert.Single(vm.Filtered));
    }

    [Fact]
    public async Task HubSearch_RequiresQueryAndFormatsResult()
    {
        var vm = new HubManagementViewModel(ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "HubSearchAsync"
            ? Task.FromResult(new CliResult { Stdout = "match", ExitCode = 1 }) : throw new InvalidOperationException(name)), NullLogger<HubManagementViewModel>.Instance);
        await vm.HubSearchAsync();
        Assert.True(vm.HasError);
        Assert.Equal("Search query is required.", vm.Message);
        vm.SearchQuery = "  ssh  ";
        await vm.HubSearchAsync();
        Assert.Contains("match", vm.DiagnosticOutput);
        Assert.Contains("exit code: 1", vm.DiagnosticOutput);
        Assert.Contains("ssh", vm.DiagnosticTitle);
        Assert.False(vm.IsBusyDiag);
    }

    [Fact]
    public async Task Install_UsesDefaultTypeAndRefreshes()
    {
        object?[]? installArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "InstallItemAsync" => Capture(args, value => installArgs = value),
            "ListHubAsync" => Task.FromResult<IReadOnlyList<CsHubItem>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new HubManagementViewModel(cli, NullLogger<HubManagementViewModel>.Instance);
        await vm.InstallAsync(new CsHubItem { Name = "collection" });
        Assert.Equal("collections", installArgs![0]);
        Assert.Equal("collection", installArgs[1]);
        Assert.Equal("Installed collection.", vm.Message);
        Assert.False(vm.HasError);
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult());
    }
}

public sealed class LapiViewModelTests
{
    [Fact]
    public async Task Refresh_FormatsSuccessAndClearsBusyFlag()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "LapiStatusAsync"
            ? Task.FromResult(new CliResult { Stdout = "online", Stderr = "notice" }) : throw new InvalidOperationException(name));
        var vm = new LapiViewModel(cli, NullLogger<LapiViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.Equal("online\n--- stderr ---\nnotice", vm.StatusOutput);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Register_ValidatesUrlAndTrimsOptionalValues()
    {
        object?[]? callArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name == "LapiRegisterAsync"
            ? Capture(args, value => callArgs = value) : throw new InvalidOperationException(name));
        var vm = new LapiViewModel(cli, NullLogger<LapiViewModel>.Instance);
        await vm.RegisterAsync();
        Assert.Equal("LAPI URL is required.", vm.Message);
        vm.RegisterMachine = " machine ";
        vm.RegisterUrl = " https://lapi.example.test ";
        vm.RegisterToken = " token ";
        await vm.RegisterAsync();
        Assert.Equal("machine", callArgs![0]);
        Assert.Equal("https://lapi.example.test", callArgs[1]);
        Assert.Equal("token", callArgs[2]);
        Assert.Contains("registered", vm.RegisterOutput);
        Assert.False(vm.IsRegistering);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ListContexts_FormatsFailure()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "LapiContextListAsync"
            ? Task.FromResult(new CliResult { ExitCode = 3, Stderr = "unavailable" }) : throw new InvalidOperationException(name));
        var vm = new LapiViewModel(cli, NullLogger<LapiViewModel>.Instance);
        await vm.ListContextsAsync();
        Assert.Contains("Exit code 3", vm.ContextsOutput);
        Assert.Contains("unavailable", vm.ContextsOutput);
        Assert.False(vm.IsLoadingContexts);
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult { Stdout = "registered" });
    }
}

public sealed class MachinesViewModelTests
{
    [Fact]
    public async Task Register_UsesTrimmedOptionalNameAndResetsFormOnSuccess()
    {
        object?[]? callArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "RegisterMachineAsync" => Capture(args, value => callArgs = value),
            "ListMachinesAsync" => Task.FromResult<IReadOnlyList<CsMachine>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new MachinesViewModel(cli, NullLogger<MachinesViewModel>.Instance) { NewMachine = " host ", AutoValidate = true };
        await vm.RegisterAsync();
        Assert.Equal("host", callArgs![0]);
        Assert.Equal(true, callArgs[1]);
        Assert.Equal("", vm.NewMachine);
        Assert.False(vm.AutoValidate);
        Assert.False(vm.IsAdding);
        Assert.Contains("registered", vm.RegisterOutput);
    }

    [Fact]
    public async Task Register_HandlesFailedCommand()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "RegisterMachineAsync"
            ? Task.FromResult(new CliResult { ExitCode = 7, Stderr = "denied" }) : throw new InvalidOperationException(name));
        var vm = new MachinesViewModel(cli, NullLogger<MachinesViewModel>.Instance);
        await vm.RegisterAsync();
        Assert.True(vm.HasError);
        Assert.Contains("denied", vm.Message);
        Assert.False(vm.IsAdding);
    }

    [Fact]
    public async Task InitializeAndPrune_LoadMachinesAndResetBusyState()
    {
        var machine = new CsMachine { MachineId = "one" };
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name switch
        {
            "ListMachinesAsync" => Task.FromResult<IReadOnlyList<CsMachine>>([machine]),
            "PruneMachinesAsync" => Task.FromResult(new CliResult()),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new MachinesViewModel(cli, NullLogger<MachinesViewModel>.Instance);

        await vm.InitializeAsync();
        Assert.Same(machine, Assert.Single(vm.Machines));
        await vm.PruneAsync();
        Assert.False(vm.HasError);
        Assert.Equal("Pruned expired machines.", vm.Message);
        Assert.False(vm.IsBusy);
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult { Stdout = "registered" });
    }
}

public sealed class NotificationsViewModelTests
{
    [Fact]
    public async Task UpdateDefaultName_TracksTypeChanges()
    {
        var vm = new NotificationsViewModel(ServiceProxy.Create<ICrowdSecCliService>((_, _) => Task.FromResult<IReadOnlyList<CsNotification>>([])),
            ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()), NullLogger<NotificationsViewModel>.Instance);
        vm.Type = "http";
        await vm.UpdateDefaultNameAsync();
        Assert.Equal("http_default", vm.Name);
        vm.Type = "email";
        await vm.UpdateDefaultNameAsync();
        Assert.Equal("email_default", vm.Name);
        vm.Type = "custom";
        await vm.UpdateDefaultNameAsync();
        Assert.Equal("slack_default", vm.Name);
    }

    [Fact]
    public async Task InitializeAndTruncate_HandleNotificationsAndBoundaries()
    {
        var item = new CsNotification();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ListNotificationsAsync"
            ? Task.FromResult<IReadOnlyList<CsNotification>>([item]) : throw new InvalidOperationException(name));
        var vm = new NotificationsViewModel(cli, ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()), NullLogger<NotificationsViewModel>.Instance);
        await vm.InitializeAsync();
        vm.Scope = "Ip";
        vm.Enabled = false;
        vm.Url = "https://example.test";
        vm.SmtpHost = "smtp.example.test";
        vm.SmtpPort = "465";
        vm.SenderEmail = "sender@example.test";
        vm.Recipients = "ops@example.test";
        vm.SmtpUser = "mailer";
        vm.SmtpPassword = "secret";
        vm.Encryption = "tls";
        Assert.Same(item, Assert.Single(vm.Notifications));
        Assert.Equal("abc...", NotificationsViewModel.Truncate("abcdef", 3));
        Assert.Null(NotificationsViewModel.Truncate(null, 3));
    }

    [Fact]
    public async Task SaveNotification_RejectsInvalidPluginWithoutWritingFiles()
    {
        var saveCount = 0;
        var config = ServiceProxy.Create<IConfigFileService>((name, _) =>
        {
            if (name == "Save") saveCount++;
            throw new InvalidOperationException(name);
        });
        var vm = new NotificationsViewModel(ServiceProxy.Create<ICrowdSecCliService>((_, _) => Task.FromResult<IReadOnlyList<CsNotification>>([])),
            config, NullLogger<NotificationsViewModel>.Instance);
        await vm.SaveNotificationAsync();
        Assert.True(vm.HasError);
        Assert.NotNull(vm.Message);
        Assert.Equal(0, saveCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SaveNotification_WritesPluginAndProfileAndRefreshes()
    {
        var savedPaths = new List<string>();
        var config = ServiceProxy.Create<IConfigFileService>((name, args) => name switch
        {
            "Load" => new ConfigFileInfo { RelativePath = "profiles.yaml.local", Content = "" },
            "Save" => Save(args, savedPaths),
            _ => throw new InvalidOperationException(name)
        });
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "ListNotificationsAsync"
            ? Task.FromResult<IReadOnlyList<CsNotification>>([]) : throw new InvalidOperationException(name));
        var vm = new NotificationsViewModel(cli, config, NullLogger<NotificationsViewModel>.Instance) { Webhook = "https://hooks.example.test/x" };

        await vm.SaveNotificationAsync();

        Assert.Contains("notifications/slack_default.yaml", savedPaths);
        Assert.Contains("profiles.yaml.local", savedPaths);
        Assert.Contains("Saved slack_default", vm.Message);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Inspect_FormatsFailedResponse()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name == "InspectNotificationAsync"
            ? Task.FromResult(new CliResult { ExitCode = 5, Stderr = "missing" }) : throw new InvalidOperationException(name));
        var vm = new NotificationsViewModel(cli, ServiceProxy.Create<IConfigFileService>((_, _) => throw new InvalidOperationException()), NullLogger<NotificationsViewModel>.Instance);
        await vm.InspectAsync("absent");
        Assert.Equal("absent", vm.InspectName);
        Assert.Contains("missing", vm.InspectOutput);
        Assert.Contains("Failed to inspect", vm.Message);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    private static object Save(object?[]? args, List<string> paths)
    {
        paths.Add((string)args![0]!);
        return (true, (string?)null);
    }
}

public sealed class SimulationViewModelTests
{
    [Fact]
    public async Task Add_RequiresScenarioAndClearsSuccessfulInput()
    {
        object?[]? addArgs = null;
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, args) => name switch
        {
            "AddSimulationAsync" => Capture(args, value => addArgs = value),
            "GetSimulationStatusAsync" => Task.FromResult(new CsSimulationStatus()),
            "ListSimulationsAsync" => Task.FromResult<IReadOnlyList<CsSimulation>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new SimulationViewModel(cli, NullLogger<SimulationViewModel>.Instance);
        await vm.AddAsync();
        Assert.Equal("Scenario name is required.", vm.Message);
        vm.NewScenario = " scenario/name ";
        await vm.AddAsync();
        Assert.Equal("scenario/name", addArgs![0]);
        Assert.Equal("", vm.NewScenario);
        Assert.False(vm.IsAdding);
    }

    [Fact]
    public async Task EnableAndDisable_ReportOperationResults()
    {
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name switch
        {
            "EnableSimulationAsync" => Task.FromResult(new CliResult()),
            "DisableSimulationAsync" => Task.FromResult(new CliResult { ExitCode = 1, Stderr = "failed" }),
            "GetSimulationStatusAsync" => Task.FromResult(new CsSimulationStatus()),
            "ListSimulationsAsync" => Task.FromResult<IReadOnlyList<CsSimulation>>([]),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new SimulationViewModel(cli, NullLogger<SimulationViewModel>.Instance);
        await vm.EnableAsync();
        Assert.False(vm.HasError);
        Assert.Contains("enabled globally", vm.Message);
        await vm.DisableAsync();
        Assert.True(vm.HasError);
        Assert.Contains("failed", vm.Message);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task InitializeAndRemove_RefreshSimulationState()
    {
        var status = new CsSimulationStatus();
        var simulation = new CsSimulation();
        var cli = ServiceProxy.Create<ICrowdSecCliService>((name, _) => name switch
        {
            "GetSimulationStatusAsync" => Task.FromResult(status),
            "ListSimulationsAsync" => Task.FromResult<IReadOnlyList<CsSimulation>>([simulation]),
            "RemoveSimulationAsync" => Task.FromResult(new CliResult()),
            _ => throw new InvalidOperationException(name)
        });
        var vm = new SimulationViewModel(cli, NullLogger<SimulationViewModel>.Instance);
        await vm.InitializeAsync();
        Assert.Same(status, vm.Status);
        Assert.Same(simulation, Assert.Single(vm.Simulations));
        await vm.RemoveAsync("scenario");
        Assert.Equal("Scenario 'scenario' removed from simulation.", vm.Message);
    }

    private static object Capture(object?[]? args, Action<object?[]> capture)
    {
        capture(args!);
        return Task.FromResult(new CliResult());
    }
}

public sealed class ThreatMapViewModelTests
{
    [Fact]
    public async Task StartMap_ReplaysSnapshotAndDeduplicatesQueuedThreats()
    {
        var stats = new ThreatMapStats { TotalAlerts = 2 };
        var snapshotThreat = new ThreatEvent { AlertId = 1, Source = new GeoPoint { Country = "FR" } };
        var bus = new FakeThreatMapEventBus { Snapshot = new([snapshotThreat], stats) };
        var js = new FakeJsRuntime();
        var vm = new ThreatMapViewModel(js,
            ServiceProxy.Create<IGeoIpService>((name, _) => name switch
            {
                "get_Home" => new GeoPoint { City = "Paris", Latitude = 48.8, Longitude = 2.3 },
                "get_IsDatabaseAvailable" => true,
                _ => throw new InvalidOperationException(name)
            }), bus, NullLogger<ThreatMapViewModel>.Instance);
        await vm.InitializeAsync();
        bus.PublishThreat(snapshotThreat, stats);
        var queued = new ThreatEvent { AlertId = 2, Source = new GeoPoint { Country = "US" } };
        bus.PublishThreat(queued, stats);

        await vm.StartMapAsync();
        await vm.StartMapAsync();

        Assert.Equal(2, vm.Recent.Count);
        Assert.Equal(2, js.Calls.Count(call => call == "threatMap.plotThreat"));
        Assert.Equal(1, vm.TopCountryMaximum);
        Assert.Equal(100, vm.CountryPercentage(1));
        Assert.Equal(2, vm.Stats.TotalAlerts);
        Assert.Equal(1, js.Calls.Count(call => call == "threatMap.init"));
        Assert.Equal("rule", ThreatMapViewModel.ScenarioName("a/b/rule"));
        Assert.Equal("—", ThreatMapViewModel.ScenarioName(null));
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromEvents()
    {
        var bus = new FakeThreatMapEventBus();
        var vm = new ThreatMapViewModel(new FakeJsRuntime(), ServiceProxy.Create<IGeoIpService>((_, _) => new GeoPoint()), bus, NullLogger<ThreatMapViewModel>.Instance);
        await vm.InitializeAsync();
        await vm.DisposeAsync();
        var stats = new ThreatMapStats { TotalAlerts = 99 };
        bus.PublishStats(stats);
        Assert.NotSame(stats, vm.Stats);
    }

    [Fact]
    public async Task OnAfterRenderAndLiveEvents_UpdateStateAndLimitCountryCount()
    {
        var bus = new FakeThreatMapEventBus();
        var js = new FakeJsRuntime();
        var vm = new ThreatMapViewModel(js, ServiceProxy.Create<IGeoIpService>((name, _) => name == "get_IsDatabaseAvailable"
            ? true : name == "get_Home" ? new GeoPoint() : throw new InvalidOperationException(name)), bus, NullLogger<ThreatMapViewModel>.Instance);
        await vm.InitializeAsync();
        await vm.OnAfterRenderAsync(firstRender: false);
        await vm.OnAfterRenderAsync(firstRender: true);
        var dispatched = 0;
        vm.SetDispatcher(async action => { dispatched++; await action(); });
        var stats = new ThreatMapStats { TotalAlerts = 42 };
        bus.PublishStats(stats);
        Assert.Same(stats, vm.Stats);

        for (var index = 0; index < 10; index++)
            bus.PublishThreat(new ThreatEvent { AlertId = index + 10, Source = new GeoPoint { Country = $"C{index}" } }, stats);

        Assert.Equal(11, dispatched);
        Assert.Equal(8, vm.TopCountries.Count);
        Assert.Equal(10, vm.Recent.Count);
        Assert.Equal(10, vm.VisibleRecent.Count);
        Assert.Equal(10, js.Calls.Count(call => call == "threatMap.plotThreat"));
        Assert.Equal(100, vm.CountryPercentage(vm.TopCountryMaximum));
    }
}
