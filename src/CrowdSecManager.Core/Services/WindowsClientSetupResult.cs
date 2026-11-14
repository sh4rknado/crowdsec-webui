namespace CrowdSecManager.Services;

public sealed record WindowsClientSetupResult(bool Success, string FileName, string? Script, string? Error);

public interface IWindowsClientProvisioningService
{
    Task<WindowsClientSetupResult> CreateSetupScriptAsync(string machineName, string lapiUrl, CancellationToken ct = default);
}
