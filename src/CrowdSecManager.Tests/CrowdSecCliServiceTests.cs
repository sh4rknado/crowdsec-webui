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
