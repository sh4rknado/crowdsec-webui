using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CrowdSecManager.Core.CommandLine;
using CrowdsecManagerModel;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

public sealed class CrowdSecCliService(IOptions<CrowdSecOptions> opt, ILogger<CrowdSecCliService> log, ICrowdSecProcessRunner processRunner) : ICrowdSecCliService
{
    private readonly CrowdSecOptions _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
    private readonly ILogger<CrowdSecCliService> _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly ICrowdSecProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

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

    public async Task<CliResult> RunTerminalAsync(string commandLine, CancellationToken ct = default)
    {
        var argv = CommandLineTokenizer.TokenizeAndStripCscli(commandLine);

        if (argv.Count == 0)
            return new CliResult { ExitCode = 0, Stdout = "", Stderr = "No command entered. Type 'help' for common cscli commands.", Duration = TimeSpan.Zero };

        var psi = new ProcessStartInfo
        {
            FileName = _opt.CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(_opt.ConfigDir) && Directory.Exists(_opt.ConfigDir))
            psi.Environment["CROWDSEC_CONFIG"] = _opt.ConfigDir;

        // Tokens are passed directly as argv - NO shell, so metacharacters such
        // as ; | & $ cannot break out of cscli. RAW output: do NOT force -o json.
        foreach (var a in argv) psi.ArgumentList.Add(a);

        try
        {
            return await _processRunner.ExecuteAsync(psi, _opt.CommandTimeoutSeconds, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Terminal command failed: {Cmd}", commandLine);
            return new CliResult { ExitCode = -1, Stderr = ex.Message, Duration = TimeSpan.Zero };
        }
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
