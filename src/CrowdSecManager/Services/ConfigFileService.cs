using CrowdSecManager.Models;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CrowdSecManager.Services;

/// <summary>Read/write CrowdSec YAML configuration files. Provides safe round-tripping
/// (load -> text area in UI -> save) and basic validation by attempting to deserialize
/// the saved document.</summary>
public interface IConfigFileService
{
    IReadOnlyList<ConfigFileInfo> ListConfigFiles();
    ConfigFileInfo? Load(string relativePath);
    (bool Valid, string? Error) Save(string relativePath, string content);
    (bool Valid, string? Error) ValidateYaml(string content);
    Task BackupAsync(string targetDir, CancellationToken ct = default);
}

public sealed class ConfigFileService : IConfigFileService
{
    private readonly CrowdSecOptions _opt;
    private readonly ILogger<ConfigFileService> _log;

    /// <summary>Whitelist of files exposed by the editor. Each is relative to <c>ConfigDir</c>.</summary>
    private static readonly (string Path, string Description)[] KnownFiles =
    {
        ("config.yaml", "Main CrowdSec configuration (api server, db, common settings)"),
        ("profiles.yaml", "Decision profiles - what to do when an alert fires"),
        ("acquis.yaml", "Log acquisition - which log files / journald / syslog to ingest"),
        ("console.yaml", "CrowdSec Console registration and sharing settings"),
        ("local_api_credentials.yaml", "Credentials used to talk to a remote Local API"),
        ("online_api_credentials.yaml", "Credentials for the CrowdSec Central API (CTI)"),
        ("simulation.yaml", "Simulation mode - dry-run scenarios without blocking"),
        ("notifications/email.yaml", "Email notification plugin configuration"),
        ("notifications/http.yaml", "HTTP webhook notification plugin configuration"),
        ("notifications/slack.yaml", "Slack notification plugin configuration"),
        ("notifications/sentinel.yaml", "Microsoft Sentinel notification plugin configuration")
    };

    public ConfigFileService(IOptions<CrowdSecOptions> opt, ILogger<ConfigFileService> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public IReadOnlyList<ConfigFileInfo> ListConfigFiles()
    {
        var list = new List<ConfigFileInfo>(KnownFiles.Length);
        foreach (var (rel, desc) in KnownFiles)
        {
            var abs = Path.Combine(_opt.ConfigDir, rel);
            var info = new ConfigFileInfo
            {
                Name = Path.GetFileName(rel),
                RelativePath = rel,
                AbsolutePath = abs,
                Description = desc,
                SizeBytes = File.Exists(abs) ? new FileInfo(abs).Length : 0,
                LastModified = File.Exists(abs) ? File.GetLastWriteTimeUtc(abs) : DateTime.MinValue
            };
            list.Add(info);
        }
        return list;
    }

    public ConfigFileInfo? Load(string relativePath)
    {
        if (!IsAllowed(relativePath)) return null;
        var abs = Path.Combine(_opt.ConfigDir, relativePath);
        if (!File.Exists(abs)) return new ConfigFileInfo
        {
            Name = Path.GetFileName(relativePath),
            RelativePath = relativePath,
            AbsolutePath = abs,
            Description = "",
            Content = "",
            SizeBytes = 0,
            LastModified = DateTime.MinValue
        };

        try
        {
            var content = File.ReadAllText(abs);
            return new ConfigFileInfo
            {
                Name = Path.GetFileName(relativePath),
                RelativePath = relativePath,
                AbsolutePath = abs,
                Description = KnownFiles.FirstOrDefault(f => f.Path == relativePath).Description ?? "",
                Content = content,
                SizeBytes = content.Length,
                LastModified = File.GetLastWriteTimeUtc(abs)
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not read {Path}", abs);
            return null;
        }
    }

    public (bool Valid, string? Error) Save(string relativePath, string content)
    {
        if (!IsAllowed(relativePath))
            return (false, "This file is not in the editable whitelist.");

        var (valid, err) = ValidateYaml(content);
        if (!valid) return (false, err);

        var abs = Path.Combine(_opt.ConfigDir, relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, content);
            _log.LogInformation("Updated CrowdSec config file {Path} ({Bytes} bytes)",
                relativePath, content.Length);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool Valid, string? Error) ValidateYaml(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return (true, null);
        try
        {
            var d = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            d.Deserialize(content);
            return (true, null);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return (false, $"YAML syntax error at line {ex.Start.Line}, col {ex.Start.Column}: {ex.Message}");
        }
    }

    public async Task BackupAsync(string targetDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var archive = Path.Combine(targetDir, $"crowdsec-config-{stamp}.tgz");
        // Best-effort tar backup (works in container, falls back to copy)
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/tar",
                Arguments = $"czf \"{archive}\" -C \"{_opt.ConfigDir}\" .",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is not null)
            {
                var err = await p.StandardError.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0)
                    _log.LogWarning("tar backup returned {Code}: {Err}", p.ExitCode, err);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "tar backup failed - copying raw files instead");
            foreach (var (rel, _) in KnownFiles)
            {
                var src = Path.Combine(_opt.ConfigDir, rel);
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(targetDir, $"{stamp}-{rel.Replace('/', '_')}");
                File.Copy(src, dst, true);
            }
        }
    }

    private bool IsAllowed(string rel)
        => KnownFiles.Any(f => string.Equals(f.Path, rel, StringComparison.OrdinalIgnoreCase));
}
