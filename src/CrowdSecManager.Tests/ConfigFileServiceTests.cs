using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Formats.Tar;
using System.IO.Compression;
using System.Diagnostics;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class ConfigFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CrowdSecManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly ConfigFileService _service;

    public ConfigFileServiceTests()
    {
        Directory.CreateDirectory(_root);
        _service = new ConfigFileService(
            Options.Create(new CrowdSecOptions { ConfigDir = _root }),
            NullLogger<ConfigFileService>.Instance);
    }

    [Fact]
    public void ValidateYaml_AcceptsValidYaml()
    {
        var (valid, error) = _service.ValidateYaml("name: crowdsec\nvalue: 3\n");

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateYaml_RejectsMalformedYaml()
    {
        var (valid, error) = _service.ValidateYaml("name: [unterminated\n");

        Assert.False(valid);
        Assert.Contains("YAML syntax error", error);
    }

    [Fact]
    public void Save_WritesWhitelistedFile()
    {
        var (valid, error) = _service.Save("profiles.yaml", "name: profile\n");

        Assert.True(valid, error);
        Assert.Equal("name: profile\n", File.ReadAllText(Path.Combine(_root, "profiles.yaml")));
    }

    [Fact]
    public void Save_WritesLocalProfilesOverrideFile()
    {
        const string content = "---\nname: manager_notification_slack_default\nfilters:\n  - Alert.Remediation == true\nnotifications:\n  - slack_default\non_success: continue\n";

        var (valid, error) = _service.Save("profiles.yaml.local", content);

        Assert.True(valid, error);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_root, "profiles.yaml.local")));
    }

    [Fact]
    public void Save_CreatesCustomYamlInEditableCrowdSecDirectory()
    {
        var path = "parsers/s00-raw/custom-parser.yaml";

        var (valid, error) = _service.Save(path, "name: custom-parser\n");

        Assert.True(valid, error);
        Assert.Equal("name: custom-parser\n", File.ReadAllText(Path.Combine(_root, "parsers", "s00-raw", "custom-parser.yaml")));
    }

    [Fact]
    public void ListConfigFiles_IncludesExistingNestedCrowdSecYaml()
    {
        var directory = Path.Combine(_root, "acquis.d");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nginx.yaml"), "source: file\n");

        var listed = _service.ListConfigFiles();

        Assert.Contains(listed, file => file.RelativePath == "acquis.d/nginx.yaml");
    }

    [Fact]
    public async Task Backup_IncludesNestedConfigurationFilesInGzipTar()
    {
        var config = Path.Combine(_root, "config.yaml");
        var nested = Path.Combine(_root, "parsers", "s00-raw", "custom.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(config, "common:");
        File.WriteAllText(nested, "name: custom");
        var backupDir = Path.Combine(_root, "backups");

        await _service.BackupAsync(backupDir);

        var archive = Assert.Single(Directory.GetFiles(backupDir, "*.tgz"));
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var entries = new Dictionary<string, long>();
        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
            entries[entry.Name.Replace('\\', '/')] = entry.Length;
        Assert.Equal(7, entries["config.yaml"]);
        Assert.Equal(12, entries["parsers/s00-raw/custom.yaml"]);
    }

    [Fact]
    public void Save_RejectsPathThroughSymlinkedDirectory()
    {
        var parsers = Path.Combine(_root, "parsers");
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, Guid.NewGuid().ToString("N"));
        var link = Path.Combine(parsers, "external");
        Directory.CreateDirectory(parsers);
        Directory.CreateDirectory(outside);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in new[] { "/c", "mklink", "/J", link, outside })
                    startInfo.ArgumentList.Add(arg);
                using var process = Process.Start(startInfo)!;
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
            else
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            Assert.Null(_service.Load("parsers/external/evil.yaml"));
            var (valid, error) = _service.Save("parsers/external/evil.yaml", "name: outside\n");

            Assert.False(valid);
            Assert.Contains("whitelist", error);
            Assert.False(File.Exists(Path.Combine(outside, "evil.yaml")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Save_RejectsMalformedYamlWithoutOverwritingExistingFile()
    {
        var path = Path.Combine(_root, "profiles.yaml");
        File.WriteAllText(path, "name: original\n");

        var (valid, _) = _service.Save("profiles.yaml", "name: [broken\n");

        Assert.False(valid);
        Assert.Equal("name: original\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("../secrets.yaml")]
    [InlineData("notifications/../../secrets.yaml")]
    [InlineData("custom.yaml")]
    [InlineData("parsers/custom.txt")]
    [InlineData("parsers/../secrets.yaml")]
    public void LoadAndSave_RejectPathsOutsideWhitelist(string path)
    {
        Assert.Null(_service.Load(path));

        var (valid, error) = _service.Save(path, "key: value\n");

        Assert.False(valid);
        Assert.Contains("whitelist", error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
