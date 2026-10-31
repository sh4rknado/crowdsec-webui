using System.Diagnostics;
using CrowdSecManager.Models;
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
    public void DecisionTargetValidation_RejectsInvalidCidrPrefix(string target)
        => Assert.False(CrowdSecCommandBuilder.IsValidDecisionTarget(target));

    private sealed class RecordingProcessRunner(string stdout) : ICrowdSecProcessRunner
    {
        public string[] Arguments { get; private set; } = Array.Empty<string>();

        public Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds,
            CancellationToken ct = default)
        {
            Arguments = startInfo.ArgumentList.ToArray();
            return Task.FromResult(new CliResult { ExitCode = 0, Stdout = stdout });
        }
    }
}
