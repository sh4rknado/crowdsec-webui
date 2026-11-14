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

    public static IReadOnlyList<string> MachinesValidate(string machineName)
        => new[] { "machines", "validate", machineName };

    public static IReadOnlyList<string> MachinesAddClient(string machineName, string lapiUrl)
        => new[] { "machines", "add", machineName, "--auto", "--url", lapiUrl, "--file", "-" };

    public static IReadOnlyList<string> MachinesPrune()
        => new[] { "machines", "prune" };

    // ---- Bouncers (cscli bouncers) ----

    public static IReadOnlyList<string> BouncersInspect(string name)
        => new[] { "bouncers", "inspect", name };

    public static IReadOnlyList<string> BouncersPrune()
        // The UI confirms this destructive action before calling cscli, so skip
        // cscli's interactive prompt (which has no stdin in the web process).
        => new[] { "bouncers", "prune", "--force" };

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
