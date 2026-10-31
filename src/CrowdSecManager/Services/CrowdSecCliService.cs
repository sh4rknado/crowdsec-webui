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
    Task<CliResult> HubUpgradeAsync(string? item = null, CancellationToken ct = default);
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

    public Task<CliResult> HubUpgradeAsync(string? item = null, CancellationToken ct = default)
    {
        var args = new List<string> { "hub", "upgrade" };
        if (!string.IsNullOrWhiteSpace(item)) args.Add(item);
        return RunAsync(args, ct);
    }

    public Task<CliResult> UpgradeItemAsync(string type, string name, CancellationToken ct = default)
    {
        type = NormalizeItemType(type);
        return RunAsync(new[] { type, "upgrade", name }, ct);
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
        var targetFlag = ip.Contains('/', StringComparison.Ordinal) ? "--range" : "--ip";
        var args = new List<string> { "decisions", "add", targetFlag, ip, "-d", duration, "-t", type };
        if (!string.IsNullOrWhiteSpace(reason)) { args.Add("-r"); args.Add(reason); }
        return RunAsync(args, ct);
    }

    public Task<CliResult> DeleteDecisionAsync(string? ip = null, long? id = null,
        bool all = false, CancellationToken ct = default)
    {
        var args = new List<string> { "decisions", "delete" };
        if (all) args.Add("--all");
        else if (id is long v) { args.Add("--id"); args.Add(v.ToString()); }
        else if (!string.IsNullOrWhiteSpace(ip)) { args.Add("-i"); args.Add(ip); }
        return RunAsync(args, ct);
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
        // Best effort: try systemctl reload crowdsec, fall back to SIGHUP
        return RunAsync(new[] { "lapi", "reload" }, ct);
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
