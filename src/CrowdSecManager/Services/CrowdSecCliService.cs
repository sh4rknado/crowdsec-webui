using System.Diagnostics;
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

public sealed class CrowdSecCliService : ICrowdSecCliService
{
    private readonly CrowdSecOptions _opt;
    private readonly ILogger<CrowdSecCliService> _log;

    public CrowdSecCliService(IOptions<CrowdSecOptions> opt, ILogger<CrowdSecCliService> log)
    {
        _opt = opt.Value;
        _log = log;
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

        var sw = Stopwatch.StartNew();
        using var p = new Process { StartInfo = psi };

        if (!p.Start())
        {
            return new CliResult { ExitCode = -1, Stderr = "Failed to start cscli", Duration = sw.Elapsed };
        }

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);

        var timeoutMs = (_opt.CommandTimeoutSeconds > 0 ? _opt.CommandTimeoutSeconds : 120) * 1000;
        var exited = p.WaitForExit(timeoutMs);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!exited)
        {
            try { p.Kill(true); } catch { }
            return new CliResult
            {
                ExitCode = -1,
                Stdout = stdout,
                Stderr = (stderr + "\n[cscli timed out]").Trim(),
                Duration = sw.Elapsed
            };
        }

        return new CliResult
        {
            ExitCode = p.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            Duration = sw.Elapsed
        };
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

    public Task<IReadOnlyList<CsHubItem>> ListHubAsync(CancellationToken ct = default)
        => RunJsonAsync<List<CsHubItem>>(new[] { "hub", "list" }, ct)
            .ContinueWith(t => (IReadOnlyList<CsHubItem>)(t.Result ?? new List<CsHubItem>()), ct);

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
        type = NormalizeItemType(type);
        return RunAsync(new[] { type, "install", name }, ct);
    }

    public Task<CliResult> RemoveItemAsync(string type, string name, CancellationToken ct = default)
    {
        type = NormalizeItemType(type);
        return RunAsync(new[] { type, "remove", name }, ct);
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
        return RunAsync(CrowdSecCommandBuilder.AddDecision(ip, duration, reason, type), ct);
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
            using var process = Process.Start(psi);
            if (process is null)
                return new CliResult { ExitCode = -1, Stderr = "Could not start the CrowdSec engine.", Duration = stopwatch.Elapsed };

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _opt.CommandTimeoutSeconds)));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new CliResult
                {
                    ExitCode = -1,
                    Stderr = ct.IsCancellationRequested ? "Configuration test cancelled." : "Configuration test timed out.",
                    Duration = stopwatch.Elapsed
                };
            }

            return new CliResult
            {
                ExitCode = process.ExitCode,
                Stdout = await stdoutTask,
                Stderr = await stderrTask,
                Duration = stopwatch.Elapsed
            };
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
            using var process = Process.Start(psi);
            if (process is null)
                return new CliResult { ExitCode = -1, Stderr = "Could not start pkill.", Duration = stopwatch.Elapsed };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _opt.CommandTimeoutSeconds)));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var message = ct.IsCancellationRequested ? "Reload cancelled." : "Reload timed out.";
                return new CliResult { ExitCode = -1, Stderr = message, Duration = stopwatch.Elapsed };
            }

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            return new CliResult
            {
                ExitCode = process.ExitCode,
                Stdout = process.ExitCode == 0 ? "Reload signal sent to CrowdSec." : "",
                Stderr = process.ExitCode == 0 ? stderr : $"No CrowdSec process was signalled. {stderr}".Trim(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not send a reload signal to CrowdSec");
            return new CliResult { ExitCode = -1, Stderr = ex.Message, Duration = stopwatch.Elapsed };
        }
    }

    private static string NormalizeItemType(string type) => type.ToLowerInvariant() switch
    {
        "collection" or "collections" => "collections",
        "parser" or "parsers" => "parsers",
        "scenario" or "scenarios" => "scenarios",
        "postoverflow" or "postoverflows" => "postoverflows",
        _ => type
    };

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
    public static IReadOnlyList<string> TestConfig(string configDir)
        => new[] { "-t", "-c", Path.Combine(configDir, "config.yaml") };

    public static IReadOnlyList<string> ReloadSignal()
        => new[] { "--signal", "HUP", "--exact", "crowdsec" };

    public static IReadOnlyList<string> UpgradeItem(string type, string name)
        => new[] { NormalizeItemType(type), "upgrade", name };

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
