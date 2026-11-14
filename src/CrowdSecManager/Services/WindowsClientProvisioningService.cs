using System.Text;
using System.Text.RegularExpressions;

namespace CrowdSecManager.Services;

public sealed class WindowsClientProvisioningService(ICrowdSecCliService cli) : IWindowsClientProvisioningService
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private static readonly Regex ValidMachineName = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$", RegexOptions.Compiled);

    public async Task<WindowsClientSetupResult> CreateSetupScriptAsync(string machineName, string lapiUrl, CancellationToken ct = default)
    {
        machineName = machineName?.Trim() ?? "";
        lapiUrl = lapiUrl?.Trim() ?? "";
        if (!ValidMachineName.IsMatch(machineName))
            return Failed("Choose a machine name using letters, numbers, dots, underscores or hyphens (1–63 characters).");

        if (!Uri.TryCreate(lapiUrl, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.UserInfo.Length > 0 ||
            endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            return Failed("Enter an HTTP or HTTPS LAPI URL reachable from the Windows computer, such as http://10.10.0.100:8080.");

        var normalizedUrl = endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var result = await _cli.AddMachineClientAsync(machineName, normalizedUrl, ct);
        if (!result.Success)
            return Failed(result.FlattenError() ?? result.Stderr ?? "CrowdSec could not register this machine.");

        var credentials = result.Stdout.Trim();
        if (!Regex.IsMatch(credentials, @"(?m)^\s*login:\s*\S+") ||
            !Regex.IsMatch(credentials, @"(?m)^\s*password:\s*\S+"))
        {
            await _cli.DeleteMachineAsync(machineName, ct);
            return Failed("CrowdSec did not return machine credentials; the incomplete machine registration was removed.");
        }

        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials + Environment.NewLine));
        var script = SetupScript.Replace("__CREDENTIALS__", encodedCredentials, StringComparison.Ordinal);
        return new WindowsClientSetupResult(true, $"install-crowdsec-{machineName}.ps1", script, null);
    }

    private static WindowsClientSetupResult Failed(string error)
        => new(false, "", null, error);

    private const string SetupScript = """
        #requires -RunAsAdministrator
        $ErrorActionPreference = 'Stop'

        Write-Host 'Downloading the official CrowdSec Windows installer...'
        $headers = @{ 'User-Agent' = 'CrowdSec-Manager-Windows-Setup' }
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/crowdsecurity/crowdsec/releases/latest' -Headers $headers
        $asset = $release.assets | Where-Object { $_.name -match '(?i)^crowdsec_.*\.msi$' } | Select-Object -First 1
        if (-not $asset) { throw 'Could not find the CrowdSec Windows MSI in the latest official release.' }

        $workDir = Join-Path $env:TEMP ('crowdsec-setup-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $workDir | Out-Null
        $installer = Join-Path $workDir $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $installer

        Write-Host 'Installing CrowdSec...'
        $install = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('"' + $installer + '"'), '/qn', '/norestart') -Wait -PassThru
        if ($install.ExitCode -notin @(0, 3010)) { throw "CrowdSec MSI installation failed (exit code $($install.ExitCode))." }

        $service = Get-Service -Name 'crowdsec' -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name 'crowdsec' -Force; $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }

        $configDir = Join-Path $env:ProgramData 'CrowdSec\config'
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        $credentialsPath = Join-Path $configDir 'local_api_credentials.yaml'
        $credentialBytes = [Convert]::FromBase64String('__CREDENTIALS__')
        [IO.File]::WriteAllBytes($credentialsPath, $credentialBytes)
        & icacls.exe $credentialsPath '/inheritance:r' '/grant:r' '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restrict access to the LAPI credentials file.' }

        Start-Service -Name 'crowdsec'
        Remove-Item -LiteralPath $workDir -Recurse -Force
        Write-Host 'CrowdSec is installed and connected to the configured Local API.'
        Write-Host 'The agent will appear in Connectors > Machines after its first heartbeat.'
        """;
}
