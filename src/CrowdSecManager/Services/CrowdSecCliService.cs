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
    Task<CliResult> AddDecisionAsync(string ip, string duration = "4h", string? reason = null, string type = "ban", CancellationToken ct = default);
    Task<CliResult> DeleteDecisionAsync(string? ip = null, long? id = null, bool all = false, CancellationToken ct = default);
    Task<IReadOnlyList<CsBouncer>> ListBouncersAsync(CancellationToken ct = default);
    Task<CliResult> AddBouncerAsync(string name, CancellationToken ct = default);
    Task<CliResult> DeleteBouncerAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<CsMachine>> ListMachinesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CsMetric>> GetMetricsAsync(CancellationToken ct = default);
    Task<CliResult> ReloadServiceAsync(CancellationToken ct = default);
    Task<CliResult> ValidateEngineConfigAsync(CancellationToken ct = default);
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
        string? reason = null, string type = "ban", CancellationToken ct = default)
    {
        if (!CrowdSecCommandBuilder.IsValidDecisionTarget(ip))
            return Task.FromResult(new CliResult
            {
                ExitCode = 2,
                Stderr = "Decision target must be a valid IPv4/IPv6 address or CIDR range."
            });

        return RunAsync(CrowdSecCommandBuilder.AddDecision(ip.Trim(), duration, reason, type), ct);
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

    public Task<CliResult> AddBouncerAsync(string name, CancellationToken ct = default)
        => RunAsync(new[] { "bouncers", "add", name }, ct);

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
        if (slash < 0) return IPAddress.TryParse(target, out _);
        if (slash == 0 || slash != target.LastIndexOf('/')) return false;
        if (!IPAddress.TryParse(target[..slash], out var address)
            || !int.TryParse(target[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix))
            return false;

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= maxPrefix;
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

    public static IReadOnlyList<string> AddDecision(string ipOrRange, string duration,
        string? reason, string type)
    {
        var targetFlag = ipOrRange.Contains('/', StringComparison.Ordinal) ? "--range" : "--ip";
        var args = new List<string> { "decisions", "add", targetFlag, ipOrRange,
            "--duration", duration, "--type", type };
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
