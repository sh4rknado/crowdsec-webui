using System.Diagnostics;
using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CrowdSecCliServiceTests
{
    [Fact]
    public async Task ListHub_RequestsAllGroupsAsJsonAndReturnsParsedAvailableItems()
    {
        var runner = new RecordingProcessRunner("""
            {
              "collections": [
                { "name": "crowdsecurity/nginx", "local_version": "1.2", "local_path": "/etc/crowdsec/collections/nginx.yaml", "description": "nginx", "status": "enabled" },
                { "name": "crowdsecurity/apache", "local_version": "", "local_path": "", "description": "apache", "status": "disabled,update-available" }
              ],
              "parsers": []
            }
            """);
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions { CliPath = "fake-cscli", ConfigDir = "" }),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var items = await service.ListHubAsync();

        Assert.Equal(new[] { "hub", "list", "-a", "-o", "json" }, runner.Arguments);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.Name == "crowdsecurity/apache" && !x.Installed);
        Assert.Contains(items, x => x.Name == "crowdsecurity/nginx" && x.Installed);
    }

    [Fact]
    public async Task RunJsonAsync_DoesNotAppendDuplicateJsonFormat()
    {
        var runner = new RecordingProcessRunner("[]");
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions { CliPath = "fake-cscli", ConfigDir = "" }),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        await service.RunJsonAsync<IReadOnlyList<string>>(new[] { "decisions", "list", "--output", "json" });

        Assert.Equal(new[] { "decisions", "list", "--output", "json" }, runner.Arguments);
    }

    [Fact]
    public async Task ProcessRunner_ReturnsStructuredFailureWhenExecutableIsMissing()
    {
        var result = await new CrowdSecProcessRunner().ExecuteAsync(
            new ProcessStartInfo { FileName = "missing-cscli-binary-for-test" }, 1);

        Assert.False(result.Success);
        Assert.Contains("Failed to start cscli", result.Stderr);
    }

    [Fact]
    public async Task ValidateEngineConfig_ExecutesConfiguredBinaryAndReturnsDiagnostics()
    {
        var runner = new RecordingProcessRunner("invalid yaml at line 3", exitCode: 1);
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions { EnginePath = "fake-crowdsec", ConfigDir = "C:/crowdsec", CommandTimeoutSeconds = 20 }),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var result = await service.ValidateEngineConfigAsync();

        Assert.False(result.Success);
        Assert.Equal("invalid yaml at line 3", result.FlattenError());
        Assert.Equal("fake-crowdsec", runner.FileName);
        Assert.Equal(new[] { "-t", "-c", Path.Combine("C:/crowdsec", "config.yaml") }, runner.Arguments);
        Assert.Equal(20, runner.TimeoutSeconds);
    }

    [Fact]
    public async Task ReloadService_ExecutesScopedSignalAndReportsSuccess()
    {
        var runner = new RecordingProcessRunner("");
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions()),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var result = await service.ReloadServiceAsync();

        Assert.True(result.Success);
        Assert.Equal("pkill", runner.FileName);
        Assert.Equal(new[] { "--signal", "HUP", "--exact", "crowdsec" }, runner.Arguments);
        Assert.Equal("Reload signal sent to CrowdSec.", result.Stdout);
    }

    [Fact]
    public async Task ReloadService_ExplainsWhenNoCrowdSecProcessWasSignalled()
    {
        var runner = new RecordingProcessRunner("", exitCode: 1, stderr: "no matching process");
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions()),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var result = await service.ReloadServiceAsync();

        Assert.False(result.Success);
        Assert.Contains("No CrowdSec process was signalled.", result.Stderr);
        Assert.Contains("no matching process", result.Stderr);
    }

    [Fact]
    public async Task AddDecision_RejectsInvalidTargetWithoutCallingCscli()
    {
        var runner = new RecordingProcessRunner("should not execute");
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions { CliPath = "fake-cscli", ConfigDir = "" }),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var result = await service.AddDecisionAsync("203.0.113.999/24");

        Assert.False(result.Success);
        Assert.Contains("valid IPv4/IPv6", result.Stderr);
        Assert.Empty(runner.Arguments);
    }

    [Theory]
    [InlineData("203.0.113.4")]
    [InlineData("2001:db8::4")]
    [InlineData("203.0.113.0/24")]
    [InlineData("2001:db8::/64")]
    public async Task AddDecision_AcceptsIpAndCidrTargets(string target)
    {
        var runner = new RecordingProcessRunner("");
        var service = new CrowdSecCliService(
            Options.Create(new CrowdSecOptions { CliPath = "fake-cscli", ConfigDir = "" }),
            NullLogger<CrowdSecCliService>.Instance,
            runner);

        var result = await service.AddDecisionAsync(target);

        Assert.True(result.Success);
        Assert.Equal(target.Contains('/') ? "--range" : "--ip", runner.Arguments[2]);
        Assert.Equal(target, runner.Arguments[3]);
    }

    [Theory]
    [InlineData("203.0.113.1/33")]
    [InlineData("2001:db8::/129")]
    [InlineData("203.0.113.1/not-a-prefix")]
    [InlineData("203.0.113.1/24/8")]
    [InlineData("1")]
    [InlineData("127.1")]
    [InlineData("0x7f000001")]
    [InlineData("0300.0250.0001.0001")]
    [InlineData("fe80::1%12")]
    [InlineData("[2001:db8::1]")]
    public void DecisionTargetValidation_RejectsUnsupportedOrInvalidTarget(string target)
        => Assert.False(CrowdSecCommandBuilder.IsValidDecisionTarget(target));

    private sealed class RecordingProcessRunner(string stdout, int exitCode = 0, string stderr = "") : ICrowdSecProcessRunner
    {
        public string[] Arguments { get; private set; } = Array.Empty<string>();
        public string FileName { get; private set; } = "";
        public int TimeoutSeconds { get; private set; }

        public Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds,
            CancellationToken ct = default)
        {
            FileName = startInfo.FileName;
            Arguments = startInfo.ArgumentList.ToArray();
            TimeoutSeconds = timeoutSeconds;
            return Task.FromResult(new CliResult { ExitCode = exitCode, Stdout = stdout, Stderr = stderr });
        }
    }
}
