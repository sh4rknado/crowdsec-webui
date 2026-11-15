using CrowdSecManager.Services;
using CrowdSecManager.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrowdSecManager.ViewModels.Tests;

public sealed class AcquisitionCreateViewModelTests
{
    [Fact]
    public async Task CreateAcquisition_ValidatesAndSavesCorrectPath()
    {
        var (config, calls) = CreateConfig();
        var vm = CreateViewModel(config, "my-app", "filenames:\n  - /var/log/app.log\nlabels:\n  type: app");

        await vm.CreateAcquisitionAsync();

        var saveCall = Assert.Single(calls, call => call.Method == "Save");
        Assert.Equal("Save", saveCall.Method);
        Assert.Equal("acquis.d/my-app.yaml", saveCall.Path);
        Assert.Equal(vm.Content, saveCall.Content);
        Assert.False(vm.HasError);
        Assert.Contains("successfully", vm.Message);
    }

    [Theory]
    [InlineData("test.yaml", "acquis.d/test.yaml")]
    [InlineData("test.yml", "acquis.d/test.yml")]
    [InlineData("test", "acquis.d/test.yaml")]
    [InlineData("acquis.d/test.yaml", "acquis.d/test.yaml")]
    public async Task CreateAcquisition_HandlesFileNameAndExtension(string input, string expected)
    {
        var (config, calls) = CreateConfig();
        var vm = CreateViewModel(config, input, "valid: yaml");

        await vm.CreateAcquisitionAsync();

        var saveCall = Assert.Single(calls, call => call.Method == "Save");
        Assert.Equal(expected, saveCall.Path);
    }

    [Fact]
    public async Task CreateAcquisition_RejectsInvalidYamlWithoutSaving()
    {
        var (config, calls) = CreateConfig(yamlValid: false, yamlError: "Invalid YAML syntax");
        var vm = CreateViewModel(config, "test.yaml", "invalid: : yaml");

        await vm.CreateAcquisitionAsync();

        Assert.True(vm.HasError);
        Assert.Equal("Invalid YAML syntax", vm.Message);
        Assert.DoesNotContain(calls, call => call.Method == "Save");
    }

    [Fact]
    public async Task CreateAcquisition_ReportsSaveFailure()
    {
        var (config, _) = CreateConfig(saveValid: false, saveError: "Permission denied");
        var vm = CreateViewModel(config, "test.yaml", "valid: yaml");

        await vm.CreateAcquisitionAsync();

        Assert.True(vm.HasError);
        Assert.Equal("Permission denied", vm.Message);
    }

    [Fact]
    public async Task CreateAcquisition_RequiresFileName()
    {
        var (config, calls) = CreateConfig();
        var vm = CreateViewModel(config, "", "valid: yaml");

        await vm.CreateAcquisitionAsync();

        Assert.True(vm.HasError);
        Assert.Equal("A file name is required.", vm.Message);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task CreateAcquisition_RequiresContent()
    {
        var (config, calls) = CreateConfig();
        var vm = CreateViewModel(config, "test.yaml", "");

        await vm.CreateAcquisitionAsync();

        Assert.True(vm.HasError);
        Assert.Equal("Acquisition content is required.", vm.Message);
        Assert.Empty(calls);
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var (config, _) = CreateConfig();
        var vm = CreateViewModel(config, "changed.yaml", "some content");

        vm.Clear();

        Assert.Equal("my-app.yaml", vm.FileName);
        Assert.Equal("", vm.Content);
        Assert.Null(vm.Message);
        Assert.False(vm.HasError);
    }

    private static AcquisitionCreateViewModel CreateViewModel(
        IConfigFileService config, string fileName, string content) =>
        new(config, NullLogger<AcquisitionCreateViewModel>.Instance)
        {
            FileName = fileName,
            Content = content
        };

    private static (IConfigFileService Config, List<Call> Calls) CreateConfig(
        bool yamlValid = true,
        string? yamlError = null,
        bool saveValid = true,
        string? saveError = null)
    {
        var calls = new List<Call>();
        var config = ServiceProxy.Create<IConfigFileService>((name, args) =>
        {
            switch (name)
            {
                case "ValidateYaml":
                    calls.Add(new Call(name, (string)args![0]!));
                    return (yamlValid, yamlError);
                case "Save":
                    calls.Add(new Call(name, (string)args![0]!, (string)args[1]!));
                    return (saveValid, saveError);
                default:
                    throw new InvalidOperationException($"Unexpected call: {name}");
            }
        });

        return (config, calls);
    }

    private sealed record Call(string Method, string Path, string Content = "");
}
