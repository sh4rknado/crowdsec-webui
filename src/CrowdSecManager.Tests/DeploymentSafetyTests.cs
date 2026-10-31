using Xunit;

namespace CrowdSecManager.Tests;

public sealed class DeploymentSafetyTests
{
    [Fact]
    public void QuadletDoesNotContainACommittedDefaultAdminPassword()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "deploy", "crowdsec-manager.container")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var quadlet = File.ReadAllText(Path.Combine(directory!.FullName, "deploy", "crowdsec-manager.container"));
        Assert.DoesNotContain("ChangeMe!StrongP@ssw0rd", quadlet, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment=ADMIN_PASSWORD=", quadlet, StringComparison.Ordinal);
    }
}
