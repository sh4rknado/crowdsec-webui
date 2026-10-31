using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
