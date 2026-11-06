using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CrowdSecManager.Core.CommandLine;
using CrowdSecManager.Models;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>Wraps the local <c>cscli</c> binary with async streaming and JSON output parsing.</summary>
public interface ICrowdSecCliService
{
    Task<CliResult> RunAsync(params string[] args);
    Task<CliResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default);
    Task<T?> RunJsonAsync<T>(IEnumerable<string> args, CancellationToken ct = default);

    /// <summary>
    /// Run an arbitrary <c>cscli</c> command line for the interactive terminal page.
    /// The input is split into argv tokens (double-quote aware, with backslash
    /// escaping), a leading literal <c>cscli</c> token is stripped if present,
    /// and the tokens are passed directly to the cscli process via
    /// <see cref="ProcessStartInfo.ArgumentList"/> (NO shell, so shell injection
    /// is impossible). Output is RAW: <c>-o json</c> is NOT forced, so the user
    /// controls formatting exactly like on a real terminal.
    /// </summary>
    Task<CliResult> RunTerminalAsync(string commandLine, CancellationToken ct = default);
    Task<IReadOnlyList<CsHubItem>> ListHubAsync(CancellationToken ct = default);
    Task<CliResult> HubUpdateAsync(CancellationToken ct = default);
    Task<CliResult> HubUpgradeAsync(CancellationToken ct = default);
    Task<CliResult> UpgradeItemAsync(string type, string name, CancellationToken ct = default);
    Task<CliResult> InstallItemAsync(string type, string name, CancellationToken ct = default);
    Task<CliResult> RemoveItemAsync(string type, string name, CancellationToken ct = default);
    Task<IReadOnlyList<CsAlert>> ListAlertsAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<CsDecision>> ListDecisionsAsync(CancellationToken ct = default);
    Task<CliResult> AddDecisionAsync(string ip, string duration = "4h", string? reason = null, string type = "ban", bool bypassAllowlist = false, CancellationToken ct = default);
    Task<CliResult> DeleteDecisionAsync(string? ip = null, long? id = null, bool all = false, CancellationToken ct = default);
    Task<IReadOnlyList<CsBouncer>> ListBouncersAsync(CancellationToken ct = default);
    Task<CliResult> AddBouncerAsync(string name, string? apiKey = null, CancellationToken ct = default);
    Task<CliResult> DeleteBouncerAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<CsMachine>> ListMachinesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CsMetric>> GetMetricsAsync(CancellationToken ct = default);
    Task<CliResult> ReloadServiceAsync(CancellationToken ct = default);
    Task<CliResult> ValidateEngineConfigAsync(CancellationToken ct = default);

    // ---- Simulation ----
    Task<IReadOnlyList<CsSimulation>> ListSimulationsAsync(CancellationToken ct = default);
    Task<CsSimulationStatus> GetSimulationStatusAsync(CancellationToken ct = default);
    Task<CliResult> EnableSimulationAsync(CancellationToken ct = default);
    Task<CliResult> DisableSimulationAsync(CancellationToken ct = default);
    Task<CliResult> AddSimulationAsync(string scenario, CancellationToken ct = default);
    Task<CliResult> RemoveSimulationAsync(string scenario, CancellationToken ct = default);

    // ---- Notifications ----
    Task<IReadOnlyList<CsNotification>> ListNotificationsAsync(CancellationToken ct = default);
    Task<CliResult> InspectNotificationAsync(string name, CancellationToken ct = default);

    // ---- Console (Central API) ----
    Task<CliResult> ConsoleStatusAsync(CancellationToken ct = default);
    Task<CliResult> ConsoleEnableAlertsAsync(CancellationToken ct = default);
    Task<CliResult> ConsoleDisableAlertsAsync(CancellationToken ct = default);

    // ---- Allowlists ----
    Task<CliResult> ListAllowlistsAsync(CancellationToken ct = default);

    // ---- Machines (extended) ----
    Task<CliResult> RegisterMachineAsync(string? machineName = null, bool autoValidate = false, CancellationToken ct = default);
    Task<CliResult> DeleteMachineAsync(string machineName, CancellationToken ct = default);
    Task<CliResult> PruneMachinesAsync(CancellationToken ct = default);

    // ---- Bouncers (extended) ----
    Task<CliResult> InspectBouncerAsync(string name, CancellationToken ct = default);
    Task<CliResult> PruneBouncersAsync(CancellationToken ct = default);

    // ---- Hub extras ----
    Task<CliResult> HubSearchAsync(string query, CancellationToken ct = default);
    Task<CliResult> HubBranchAsync(CancellationToken ct = default);
    Task<CliResult> HubTypesAsync(CancellationToken ct = default);

    // ---- Config (cscli config) ----
    Task<CliResult> ConfigShowAsync(CancellationToken ct = default);
    Task<CliResult> ConfigShowYamlAsync(CancellationToken ct = default);
    Task<CliResult> ConfigBackupAsync(string targetFile, CancellationToken ct = default);
    Task<CliResult> ConfigRestoreAsync(string sourceFile, CancellationToken ct = default);
    Task<CliResult> ConfigFeatureFlagsAsync(CancellationToken ct = default);

    // ---- LAPI (cscli lapi) ----
    Task<CliResult> LapiStatusAsync(CancellationToken ct = default);
    Task<CliResult> LapiRegisterAsync(string? machineName, string? url, string? token, CancellationToken ct = default);
    Task<CliResult> LapiContextListAsync(CancellationToken ct = default);
    Task<CliResult> LapiContextShowAsync(string name, CancellationToken ct = default);

    // ---- Explain (cscli explain) - test the log pipeline ----
    Task<CliResult> ExplainLogLineAsync(string logLine, string type, string? labels = null, CancellationToken ct = default);
    Task<CliResult> ExplainLogFileAsync(string filePath, string type, bool verbose = false, CancellationToken ct = default);

    // ---- Alerts/Decisions with filters ----
    Task<IReadOnlyList<CsAlert>> ListAlertsAsync(int limit, string? ip, string? scenario, string? since, string? until, string? type, CancellationToken ct = default);
    Task<IReadOnlyList<CsDecision>> ListDecisionsAsync(string? ip, string? scenario, string? since, string? until, string? type, CancellationToken ct = default);
}
