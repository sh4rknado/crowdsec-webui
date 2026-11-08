using CrowdsecManagerModel;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CliResultTests
{
    [Fact]
    public void FlattenError_PrefersStandardError()
    {
        var result = new CliResult { ExitCode = 1, Stdout = "informational output", Stderr = "  config failed  " };

        Assert.Equal("config failed", result.FlattenError());
    }

    [Fact]
    public void FlattenError_FallsBackToStandardOutputWhenStandardErrorIsEmpty()
    {
        var result = new CliResult { ExitCode = 1, Stdout = "  invalid configuration at line 4  " };

        Assert.Equal("invalid configuration at line 4", result.FlattenError());
    }

    [Fact]
    public void FlattenError_ReturnsNullForSuccessfulCommand()
    {
        var result = new CliResult { ExitCode = 0, Stdout = "success output", Stderr = "warning" };

        Assert.Null(result.FlattenError());
    }

    [Fact]
    public void FlattenError_ReturnsNullWhenFailedCommandHasNoOutput()
    {
        var result = new CliResult { ExitCode = 2 };

        Assert.Null(result.FlattenError());
    }
}
