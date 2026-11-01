using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CrowdSecManager.Models;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>Wraps the local <c>cscli</c> binary with async streaming and JSON output parsing.</summary>
public interface ICrowdSecCliService
{
    Task<CliResult> RunAsync(params string[] args);
    Task<CliResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default);
    Task<T?> RunJsonAsync<T>(IEnumerable<string> args, CancellationToken ct = default);
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

public interface ICrowdSecProcessRunner
{
    Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds, CancellationToken ct = default);
}

public sealed class CrowdSecProcessRunner : ICrowdSecProcessRunner
{
    public async Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return new CliResult { ExitCode = -1, Stderr = "Failed to start cscli", Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            return new CliResult
            {
                ExitCode = -1,
                Stderr = $"Failed to start cscli: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            var message = ct.IsCancellationRequested ? "cscli command cancelled." : "cscli timed out.";
            return new CliResult { ExitCode = -1, Stderr = message, Duration = stopwatch.Elapsed };
        }

        return new CliResult
        {
            ExitCode = process.ExitCode,
            Stdout = await stdoutTask,
            Stderr = await stderrTask,
            Duration = stopwatch.Elapsed
        };
    }
}

public sealed class CrowdSecCliService : ICrowdSecCliService
{
    private readonly CrowdSecOptions _opt;
    private readonly ILogger<CrowdSecCliService> _log;
    private readonly ICrowdSecProcessRunner _processRunner;

    public CrowdSecCliService(IOptions<CrowdSecOptions> opt, ILogger<CrowdSecCliService> log,
        ICrowdSecProcessRunner processRunner)
    {
        _opt = opt.Value;
        _log = log;
        _processRunner = processRunner;
    }

    public Task<CliResult> RunAsync(params string[] args) => RunAsync((IEnumerable<string>)args);

    public async Task<CliResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var argv = args.ToArray();
        var psi = new ProcessStartInfo
        {
            FileName = _opt.CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(_opt.ConfigDir) && Directory.Exists(_opt.ConfigDir))
        {
            psi.Environment["CROWDSEC_CONFIG"] = _opt.ConfigDir;
        }

        foreach (var a in argv) psi.ArgumentList.Add(a);

        // Always request JSON output for parseable commands
        var hasFormat = argv.Any(a => a == "-o" || a == "--output");
        var knownJsonCmd = argv.Length > 0 && JsonCommands.Contains(argv[0]);
        if (knownJsonCmd && !hasFormat)
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("json");
        }

        return await _processRunner.ExecuteAsync(psi, _opt.CommandTimeoutSeconds, ct);
    }

    public async Task<T?> RunJsonAsync<T>(IEnumerable<string> args, CancellationToken ct = default)
    {
        var argv = args.ToList();
        var hasFormat = argv.Any(a => a == "-o" || a == "--output");
        if (!hasFormat) { argv.Add("-o"); argv.Add("json"); }

        var r = await RunAsync(argv, ct);
        if (!r.Success) return default;

        var raw = r.Stdout.Trim();
        if (raw.Length == 0) return default;
        if (raw == "null" || raw == "[]") return default;

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Could not parse cscli JSON output (cmd: {Cmd}): {Snippet}",
                string.Join(' ', argv), raw.Length > 200 ? raw[..200] : raw);
            return default;
        }
    }

    public async Task<IReadOnlyList<CsHubItem>> ListHubAsync(CancellationToken ct = default)
    {
        // -a includes available as well as installed Hub items, allowing the UI
        // to offer install actions. CrowdSec JSON is grouped by type, not a list.
        var result = await RunAsync(new[] { "hub", "list", "-a" }, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Stdout)) return Array.Empty<CsHubItem>();

        try { return CrowdSecHubParser.Parse(result.Stdout); }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Could not parse cscli Hub JSON output");
            return Array.Empty<CsHubItem>();
        }
    }

    public Task<CliResult> HubUpdateAsync(CancellationToken ct = default)
        => RunAsync(new[] { "hub", "update" }, ct);

    public Task<CliResult> HubUpgradeAsync(CancellationToken ct = default)
        => RunAsync(new[] { "hub", "upgrade" }, ct);

    public Task<CliResult> UpgradeItemAsync(string type, string name, CancellationToken ct = default)
    {
        return RunAsync(CrowdSecCommandBuilder.UpgradeItem(type, name), ct);
    }

    public Task<CliResult> InstallItemAsync(string type, string name, CancellationToken ct = default)
    {
        return RunAsync(CrowdSecCommandBuilder.InstallItem(type, name), ct);
    }

    public Task<CliResult> RemoveItemAsync(string type, string name, CancellationToken ct = default)
    {
        return RunAsync(CrowdSecCommandBuilder.RemoveItem(type, name), ct);
    }

    public async Task<IReadOnlyList<CsAlert>> ListAlertsAsync(int limit = 100, CancellationToken ct = default)
    {
        var args = new List<string> { "alerts", "list", "-l", limit.ToString() };
        var alerts = await RunJsonAsync<List<CsAlert>>(args, ct);
        return alerts ?? new List<CsAlert>();
    }

    public async Task<IReadOnlyList<CsDecision>> ListDecisionsAsync(CancellationToken ct = default)
    {
        var decisions = await RunJsonAsync<List<CsDecision>>(new[] { "decisions", "list" }, ct);
        return decisions ?? new List<CsDecision>();
    }

    public Task<CliResult> AddDecisionAsync(string ip, string duration = "4h",
        string? reason = null, string type = "ban", bool bypassAllowlist = false, CancellationToken ct = default)
    {
        if (!CrowdSecCommandBuilder.IsValidDecisionTarget(ip))
            return Task.FromResult(new CliResult
            {
                ExitCode = 2,
                Stderr = "Decision target must be a valid IPv4/IPv6 address or CIDR range."
            });

        return RunAsync(CrowdSecCommandBuilder.AddDecision(ip.Trim(), duration, reason, type, bypassAllowlist), ct);
    }

    public Task<CliResult> DeleteDecisionAsync(string? ip = null, long? id = null,
        bool all = false, CancellationToken ct = default)
    {
        return RunAsync(CrowdSecCommandBuilder.DeleteDecision(ip, id, all), ct);
    }

    public async Task<IReadOnlyList<CsBouncer>> ListBouncersAsync(CancellationToken ct = default)
    {
        var b = await RunJsonAsync<List<CsBouncer>>(new[] { "bouncers", "list" }, ct);
        return b ?? new List<CsBouncer>();
    }

    public Task<CliResult> AddBouncerAsync(string name, string? apiKey = null, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.BouncersAdd(name, apiKey), ct);

    public Task<CliResult> DeleteBouncerAsync(string name, CancellationToken ct = default)
        => RunAsync(new[] { "bouncers", "delete", name }, ct);

    public async Task<IReadOnlyList<CsMachine>> ListMachinesAsync(CancellationToken ct = default)
    {
        var m = await RunJsonAsync<List<CsMachine>>(new[] { "machines", "list" }, ct);
        return m ?? new List<CsMachine>();
    }

    public async Task<IReadOnlyList<CsMetric>> GetMetricsAsync(CancellationToken ct = default)
    {
        var m = await RunJsonAsync<List<CsMetric>>(new[] { "metrics" }, ct);
        return m ?? new List<CsMetric>();
    }

    // ---- Simulation ----

    public async Task<IReadOnlyList<CsSimulation>> ListSimulationsAsync(CancellationToken ct = default)
    {
        var list = await RunJsonAsync<List<CsSimulation>>(CrowdSecCommandBuilder.SimulationList(), ct);
        return list ?? new List<CsSimulation>();
    }

    public async Task<CsSimulationStatus> GetSimulationStatusAsync(CancellationToken ct = default)
    {
        // cscli simulation status has no JSON output; we parse the human form.
        var r = await RunAsync(CrowdSecCommandBuilder.SimulationStatus(), ct);
        var status = new CsSimulationStatus { RawOutput = r.Stdout };
        if (!r.Success) return status;
        // Match common CrowdSec wording: "Global simulation: enabled" or "...: disabled"
        var match = System.Text.RegularExpressions.Regex.Match(
            r.Stdout + " " + r.Stderr,
            @"Global simulation[^:]*:\s*(enabled|disabled|on|off|true|false)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var v = match.Groups[1].Value.ToLowerInvariant();
            status.GloballyEnabled = v is "enabled" or "on" or "true";
        }
        return status;
    }

    public Task<CliResult> EnableSimulationAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.SimulationEnable(), ct);

    public Task<CliResult> DisableSimulationAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.SimulationDisable(), ct);

    public Task<CliResult> AddSimulationAsync(string scenario, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.SimulationAdd(scenario), ct);

    public Task<CliResult> RemoveSimulationAsync(string scenario, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.SimulationRemove(scenario), ct);

    // ---- Notifications ----

    public async Task<IReadOnlyList<CsNotification>> ListNotificationsAsync(CancellationToken ct = default)
    {
        var list = await RunJsonAsync<List<CsNotification>>(CrowdSecCommandBuilder.NotificationsList(), ct);
        return list ?? new List<CsNotification>();
    }

    public Task<CliResult> InspectNotificationAsync(string name, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.NotificationsInspect(name), ct);

    // ---- Console (Central API) ----

    public Task<CliResult> ConsoleStatusAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConsoleStatus(), ct);

    public Task<CliResult> ConsoleEnableAlertsAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConsoleEnable(), ct);

    public Task<CliResult> ConsoleDisableAlertsAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConsoleDisable(), ct);

    // ---- Allowlists ----

    public Task<CliResult> ListAllowlistsAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.AllowlistsList(), ct);

    // ---- Machines (extended) ----

    public Task<CliResult> RegisterMachineAsync(string? machineName = null, bool autoValidate = false, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.MachinesRegister(machineName, autoValidate), ct);

    public Task<CliResult> DeleteMachineAsync(string machineName, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.MachinesDelete(machineName), ct);

    public Task<CliResult> PruneMachinesAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.MachinesPrune(), ct);

    // ---- Bouncers (extended) ----

    public Task<CliResult> InspectBouncerAsync(string name, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.BouncersInspect(name), ct);

    public Task<CliResult> PruneBouncersAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.BouncersPrune(), ct);

    // ---- Hub extras ----

    public Task<CliResult> HubSearchAsync(string query, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.HubSearch(query), ct);

    public Task<CliResult> HubBranchAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.HubBranch(), ct);

    public Task<CliResult> HubTypesAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.HubTypes(), ct);

    // ---- Config (cscli config) ----

    public Task<CliResult> ConfigShowAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConfigShow(), ct);

    public Task<CliResult> ConfigShowYamlAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConfigShowYaml(), ct);

    public Task<CliResult> ConfigBackupAsync(string targetFile, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConfigBackup(targetFile), ct);

    public Task<CliResult> ConfigRestoreAsync(string sourceFile, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConfigRestore(sourceFile), ct);

    public Task<CliResult> ConfigFeatureFlagsAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ConfigFeatureFlags(), ct);

    // ---- LAPI (cscli lapi) ----

    public Task<CliResult> LapiStatusAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.LapiStatus(), ct);

    public Task<CliResult> LapiRegisterAsync(string? machineName, string? url, string? token, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.LapiRegister(machineName, url, token), ct);

    public Task<CliResult> LapiContextListAsync(CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.LapiContextList(), ct);

    public Task<CliResult> LapiContextShowAsync(string name, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.LapiContextShow(name), ct);

    // ---- Explain (cscli explain) - test the log pipeline ----

    public Task<CliResult> ExplainLogLineAsync(string logLine, string type, string? labels = null, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ExplainLogLine(logLine, type, labels), ct);

    public Task<CliResult> ExplainLogFileAsync(string filePath, string type, bool verbose = false, CancellationToken ct = default)
        => RunAsync(CrowdSecCommandBuilder.ExplainLogFile(filePath, type, verbose), ct);

    // ---- Alerts/Decisions with filters ----

    public async Task<IReadOnlyList<CsAlert>> ListAlertsAsync(int limit, string? ip, string? scenario,
        string? since, string? until, string? type, CancellationToken ct = default)
    {
        var alerts = await RunJsonAsync<List<CsAlert>>(
            CrowdSecCommandBuilder.AlertsList(limit, ip, scenario, since, until, type), ct);
        return alerts ?? new List<CsAlert>();
    }

    public async Task<IReadOnlyList<CsDecision>> ListDecisionsAsync(string? ip, string? scenario,
        string? since, string? until, string? type, CancellationToken ct = default)
    {
        var decisions = await RunJsonAsync<List<CsDecision>>(
            CrowdSecCommandBuilder.DecisionsList(ip, scenario, since, until, type), ct);
        return decisions ?? new List<CsDecision>();
    }

    public Task<CliResult> ReloadServiceAsync(CancellationToken ct = default)
    {
        return SendCrowdSecReloadAsync(ct);
    }

    public async Task<CliResult> ValidateEngineConfigAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = _opt.EnginePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in CrowdSecCommandBuilder.TestConfig(_opt.ConfigDir)) psi.ArgumentList.Add(arg);

        try
        {
            return await _processRunner.ExecuteAsync(psi, Math.Max(1, _opt.CommandTimeoutSeconds), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not test CrowdSec configuration");
            return new CliResult { ExitCode = -1, Stderr = ex.Message, Duration = stopwatch.Elapsed };
        }
    }

    private async Task<CliResult> SendCrowdSecReloadAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = "pkill",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in CrowdSecCommandBuilder.ReloadSignal()) psi.ArgumentList.Add(arg);

        try
        {
            var result = await _processRunner.ExecuteAsync(psi, Math.Max(1, _opt.CommandTimeoutSeconds), ct);
            if (result.Success)
            {
                result.Stdout = "Reload signal sent to CrowdSec.";
                return result;
            }

            var detail = result.FlattenError();
            result.Stderr = string.IsNullOrWhiteSpace(detail)
                ? "No CrowdSec process was signalled."
                : $"No CrowdSec process was signalled. {detail}";
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not send a reload signal to CrowdSec");
            return new CliResult { ExitCode = -1, Stderr = ex.Message, Duration = stopwatch.Elapsed };
        }
    }

    private static readonly HashSet<string> JsonCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "hub", "alerts", "decisions", "bouncers", "machines", "metrics", "config"
    };

    // Commands where only some subcommands support -o json (e.g. simulation list does,
    // simulation status does not). RunJsonAsync appends -o json explicitly when needed,
    // so these are intentionally NOT in JsonCommands to avoid breaking non-JSON subcommands.
    // Affected: simulation, notifications, allowlists, console.

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

/// <summary>Builds argument vectors for commands that modify CrowdSec state.</summary>
public static class CrowdSecCommandBuilder
{
    public static bool IsValidDecisionTarget(string? ipOrRange)
    {
        if (string.IsNullOrWhiteSpace(ipOrRange)) return false;
        var target = ipOrRange.Trim();
        var slash = target.IndexOf('/');
        if (slash < 0) return IsSupportedDecisionAddress(target, out _);
        if (slash == 0 || slash != target.LastIndexOf('/')) return false;
        if (!IsSupportedDecisionAddress(target[..slash], out var address)
            || !int.TryParse(target[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix))
            return false;

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= maxPrefix;
    }

    private static bool IsSupportedDecisionAddress(string value, out IPAddress address)
    {
        address = IPAddress.None;
        if (!IPAddress.TryParse(value, out var parsed)) return false;

        if (parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            // .NET accepts legacy shorthand, octal and hexadecimal IPv4 forms;
            // CrowdSec operators expect conventional four-octet dotted decimal.
            if (!string.Equals(value, parsed.ToString(), StringComparison.Ordinal)) return false;
            address = parsed;
            return true;
        }

        if (parsed.AddressFamily != AddressFamily.InterNetworkV6
            || !value.Contains(':')
            || value.Contains('%')
            || value.Contains('[')
            || value.Contains(']'))
            return false;

        address = parsed;
        return true;
    }

    public static IReadOnlyList<string> TestConfig(string configDir)
        => new[] { "-t", "-c", Path.Combine(configDir, "config.yaml") };

    public static IReadOnlyList<string> ReloadSignal()
        => new[] { "--signal", "HUP", "--exact", "crowdsec" };

    public static IReadOnlyList<string> UpgradeItem(string type, string name)
        => new[] { NormalizeItemType(type), "upgrade", name };

    public static IReadOnlyList<string> InstallItem(string type, string name)
        => new[] { NormalizeItemType(type), "install", name };

    public static IReadOnlyList<string> RemoveItem(string type, string name)
        => new[] { NormalizeItemType(type), "remove", name };

    public static IReadOnlyList<string> DeleteAlert(long id)
        => new[] { "alerts", "delete", "--id", id.ToString() };

    // ---- Simulation (cscli simulation) ----

    public static IReadOnlyList<string> SimulationList()
        => new[] { "simulation", "list" };

    public static IReadOnlyList<string> SimulationStatus()
        => new[] { "simulation", "status" };

    public static IReadOnlyList<string> SimulationEnable()
        => new[] { "simulation", "enable" };

    public static IReadOnlyList<string> SimulationDisable()
        => new[] { "simulation", "disable" };

    public static IReadOnlyList<string> SimulationAdd(string scenario)
        => new[] { "simulation", "add", scenario };

    public static IReadOnlyList<string> SimulationRemove(string scenario)
        => new[] { "simulation", "remove", scenario };

    // ---- Notifications (cscli notifications) ----

    public static IReadOnlyList<string> NotificationsList()
        => new[] { "notifications", "list" };

    public static IReadOnlyList<string> NotificationsInspect(string name)
        => new[] { "notifications", "inspect", name };

    // ---- Console (cscli console) ----

    public static IReadOnlyList<string> ConsoleStatus()
        => new[] { "console", "status" };

    public static IReadOnlyList<string> ConsoleEnable()
        => new[] { "console", "enable", "alerts" };

    public static IReadOnlyList<string> ConsoleDisable()
        => new[] { "console", "disable", "alerts" };

    // ---- Allowlists (cscli allowlists / decisions --bypass-allowlist) ----

    public static IReadOnlyList<string> AllowlistsList()
        => new[] { "allowlists", "list" };

    // ---- Machines (cscli machines) ----

    public static IReadOnlyList<string> MachinesList()
        => new[] { "machines", "list" };

    /// <param name="machineName">Optional machine name. When omitted, cscli generates one.</param>
    /// <param name="autoValidate">If true, add --auto to skip manual validation.</param>
    public static IReadOnlyList<string> MachinesRegister(string? machineName = null, bool autoValidate = false)
    {
        var args = new List<string> { "machines", "register" };
        if (!string.IsNullOrWhiteSpace(machineName)) { args.Add("--machine"); args.Add(machineName); }
        if (autoValidate) args.Add("--auto");
        return args;
    }

    public static IReadOnlyList<string> MachinesDelete(string machineName)
        => new[] { "machines", "delete", machineName };

    public static IReadOnlyList<string> MachinesPrune()
        => new[] { "machines", "prune" };

    // ---- Bouncers (cscli bouncers) ----

    public static IReadOnlyList<string> BouncersInspect(string name)
        => new[] { "bouncers", "inspect", name };

    public static IReadOnlyList<string> BouncersPrune()
        => new[] { "bouncers", "prune" };

    /// <summary>Build "cscli bouncers add NAME [--key KEY]".
    /// The bouncer name is a required positional argument (cscli uses args.ExactArgs(1)).
    /// When <paramref name="apiKey"/> is null or empty, cscli generates one and prints it
    /// to stdout. When provided, cscli uses it verbatim.</summary>
    public static IReadOnlyList<string> BouncersAdd(string name, string? apiKey = null)
    {
        var args = new List<string> { "bouncers", "add", name };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            args.Add("--key");
            args.Add(apiKey);
        }
        return args;
    }

    // ---- Hub extras ----

    public static IReadOnlyList<string> HubSearch(string query)
        => new[] { "hub", "search", query };

    public static IReadOnlyList<string> HubBranch()
        => new[] { "hub", "branch" };

    public static IReadOnlyList<string> HubTypes()
        => new[] { "hub", "types" };

    // ---- Config (cscli config) ----

    public static IReadOnlyList<string> ConfigShow()
        => new[] { "config", "show" };

    public static IReadOnlyList<string> ConfigShowYaml()
        => new[] { "config", "show-yaml" };

    public static IReadOnlyList<string> ConfigBackup(string targetFile)
        => new[] { "config", "backup", targetFile };

    public static IReadOnlyList<string> ConfigRestore(string sourceFile)
        => new[] { "config", "restore", sourceFile };

    public static IReadOnlyList<string> ConfigFeatureFlags()
        => new[] { "config", "feature-flags" };

    // ---- LAPI (cscli lapi) ----

    public static IReadOnlyList<string> LapiStatus()
        => new[] { "lapi", "status" };

    public static IReadOnlyList<string> LapiRegister(string? machineName, string? url, string? token)
    {
        var args = new List<string> { "lapi", "register" };
        if (!string.IsNullOrWhiteSpace(machineName)) { args.Add("--machine"); args.Add(machineName); }
        if (!string.IsNullOrWhiteSpace(url)) { args.Add("--url"); args.Add(url); }
        if (!string.IsNullOrWhiteSpace(token)) { args.Add("--token"); args.Add(token); }
        return args;
    }

    public static IReadOnlyList<string> LapiContextList()
        => new[] { "lapi", "context", "list" };

    public static IReadOnlyList<string> LapiContextShow(string name)
        => new[] { "lapi", "context", "show", name };

    // ---- Explain (cscli explain) - test the log pipeline ----

    /// <summary>Build the cscli explain command for a single log line.</summary>
    public static IReadOnlyList<string> ExplainLogLine(string logLine, string type, string? labels = null)
    {
        var args = new List<string> { "explain", "--log", logLine, "--type", type };
        if (!string.IsNullOrWhiteSpace(labels)) { args.Add("--labels"); args.Add(labels); }
        return args;
    }

    /// <summary>Build the cscli explain command for a log file.</summary>
    public static IReadOnlyList<string> ExplainLogFile(string filePath, string type, bool verbose = false)
    {
        var args = new List<string> { "explain", "--file", filePath, "--type", type };
        if (verbose) args.Add("--verbose");
        return args;
    }

    // ---- Alerts/Decisions filters (cscli alerts list / decisions list) ----

    /// <summary>Build the cscli alerts list command with optional filters.</summary>
    public static IReadOnlyList<string> AlertsList(int limit, string? ip = null, string? scenario = null,
        string? since = null, string? until = null, string? type = null)
    {
        var args = new List<string> { "alerts", "list", "-l", limit.ToString() };
        if (!string.IsNullOrWhiteSpace(ip)) { args.Add("--ip"); args.Add(ip); }
        if (!string.IsNullOrWhiteSpace(scenario)) { args.Add("--scenario"); args.Add(scenario); }
        if (!string.IsNullOrWhiteSpace(since)) { args.Add("--since"); args.Add(since); }
        if (!string.IsNullOrWhiteSpace(until)) { args.Add("--until"); args.Add(until); }
        if (!string.IsNullOrWhiteSpace(type)) { args.Add("--type"); args.Add(type); }
        return args;
    }

    /// <summary>Build the cscli decisions list command with optional filters.</summary>
    public static IReadOnlyList<string> DecisionsList(string? ip = null, string? scenario = null,
        string? since = null, string? until = null, string? type = null)
    {
        var args = new List<string> { "decisions", "list" };
        if (!string.IsNullOrWhiteSpace(ip)) { args.Add("--ip"); args.Add(ip); }
        if (!string.IsNullOrWhiteSpace(scenario)) { args.Add("--scenario"); args.Add(scenario); }
        if (!string.IsNullOrWhiteSpace(since)) { args.Add("--since"); args.Add(since); }
        if (!string.IsNullOrWhiteSpace(until)) { args.Add("--until"); args.Add(until); }
        if (!string.IsNullOrWhiteSpace(type)) { args.Add("--type"); args.Add(type); }
        return args;
    }

    public static IReadOnlyList<string> AddDecision(string ipOrRange, string duration,
        string? reason, string type)
        => AddDecision(ipOrRange, duration, reason, type, bypassAllowlist: false);

    public static IReadOnlyList<string> AddDecision(string ipOrRange, string duration,
        string? reason, string type, bool bypassAllowlist)
    {
        var targetFlag = ipOrRange.Contains('/', StringComparison.Ordinal) ? "--range" : "--ip";
        var args = new List<string> { "decisions", "add", targetFlag, ipOrRange,
            "--duration", duration, "--type", type };
        if (bypassAllowlist)
        {
            args.Add("--bypass-allowlist");
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            args.Add("--reason");
            args.Add(reason);
        }
        return args;
    }

    public static IReadOnlyList<string> DeleteDecision(string? ip = null, long? id = null, bool all = false)
    {
        var args = new List<string> { "decisions", "delete" };
        if (all) args.Add("--all");
        else if (id is long value) { args.Add("--id"); args.Add(value.ToString()); }
        else if (!string.IsNullOrWhiteSpace(ip)) { args.Add("--ip"); args.Add(ip); }
        return args;
    }

    private static string NormalizeItemType(string type) => type.ToLowerInvariant() switch
    {
        "collection" or "collections" => "collections",
        "parser" or "parsers" => "parsers",
        "scenario" or "scenarios" => "scenarios",
        "postoverflow" or "postoverflows" => "postoverflows",
        _ => type
    };
}

/// <summary>Parses the category-grouped object returned by <c>cscli hub list -o json</c>.</summary>
public static class CrowdSecHubParser
{
    private static readonly JsonSerializerOptions HubJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static IReadOnlyList<CsHubItem> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object grouped by Hub item type.");

        var items = new List<CsHubItem>();
        foreach (var group in document.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var element in group.Value.EnumerateArray())
            {
                var item = element.Deserialize<CsHubItem>(HubJsonOptions);
                if (item is null) continue;
                item.Type = group.Name;
                item.Status ??= "";
                item.Installed = !string.IsNullOrWhiteSpace(item.LocalVersion);
                item.Tainted = item.Status.Contains("tainted", StringComparison.OrdinalIgnoreCase);
                item.UpToDate = item.Installed
                    && !item.Status.Contains("update-available", StringComparison.OrdinalIgnoreCase);
                items.Add(item);
            }
        }

        return items;
    }
}
