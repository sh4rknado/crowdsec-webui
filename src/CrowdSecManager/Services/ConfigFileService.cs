using CrowdSecManager.Models;
using System.Formats.Tar;
using System.IO.Compression;
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

    /// <summary>
    /// Create a brand-new YAML file inside an editable directory (e.g. <c>acquis.d/</c>).
    /// Fails if the file already exists, if the path is outside the editable
    /// directories, or if the filename is not a .yaml/.yml file. Enables the
    /// "add new acquisition.yaml" workflow from the web UI without dropping a
    /// file on the host manually.
    /// </summary>
    /// <param name="relativePath">Path relative to <c>ConfigDir</c>, e.g. <c>acquis.d/my-app.yaml</c>.</param>
    /// <param name="error">Human-readable reason when creation is refused.</param>
    (bool Created, string? Error) TryCreate(string relativePath, out string? error);
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

    private static readonly string[] EditableDirectories =
    {
        "acquis.d", "notifications", "parsers", "scenarios", "postoverflows",
        "collections", "contexts", "appsec-rules", "appsec-configs"
    };

    public ConfigFileService(IOptions<CrowdSecOptions> opt, ILogger<ConfigFileService> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public IReadOnlyList<ConfigFileInfo> ListConfigFiles()
    {
        var list = new List<ConfigFileInfo>(KnownFiles.Length);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, desc) in KnownFiles)
        {
            included.Add(rel);
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

        foreach (var directory in EditableDirectories)
        {
            var absoluteDirectory = Path.Combine(_opt.ConfigDir, directory);
            if (!Directory.Exists(absoluteDirectory)) continue;

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            try
            {
                foreach (var absolutePath in Directory.EnumerateFiles(absoluteDirectory, "*", enumerationOptions))
                {
                    if (!IsYamlPath(absolutePath)) continue;
                    var rel = Path.GetRelativePath(_opt.ConfigDir, absolutePath).Replace('\\', '/');
                    if (!included.Add(rel)) continue;
                    var file = new FileInfo(absolutePath);
                    list.Add(new ConfigFileInfo
                    {
                        Name = Path.GetFileName(rel),
                        RelativePath = rel,
                        AbsolutePath = absolutePath,
                        Description = $"CrowdSec YAML in {directory}",
                        SizeBytes = file.Length,
                        LastModified = file.LastWriteTimeUtc,
                        Content = ""
                    });
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not enumerate CrowdSec config directory {Directory}", absoluteDirectory);
            }
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
            var temporaryPath = abs + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, content);
                File.Move(temporaryPath, abs, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            _log.LogInformation("Updated CrowdSec config file {Path} ({Bytes} bytes)",
                relativePath, content.Length);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool Created, string? Error) TryCreate(string relativePath, out string? error)
    {
        // Reuse the same path-traversal + whitelist guard as Save. TryCreate is
        // stricter on two points: the file must NOT already exist, and it must
        // live inside an EditableDirectory (so users cannot overwrite the
        // top-level acquis.yaml / config.yaml with a fresh empty file by accident).
        if (!IsAllowed(relativePath))
        {
            error = "New files may only be created inside an editable directory "
                    + "(acquis.d, notifications, parsers, scenarios, postoverflows, "
                    + "collections, contexts, appsec-rules, appsec-configs) and must end in .yaml/.yml.";
            return (false, error);
        }

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            error = "Use the editor to modify the top-level file. New files must live in a sub-directory (e.g. acquis.d/my-app.yaml).";
            return (false, error);
        }
        if (!EditableDirectories.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            error = $"Cannot create files under '{segments[0]}'. Use one of: {string.Join(", ", EditableDirectories)}.";
            return (false, error);
        }

        var abs = Path.Combine(_opt.ConfigDir, relativePath);
        try
        {
            if (File.Exists(abs))
            {
                error = $"A file already exists at {relativePath}. Open it from the list to edit instead.";
                return (false, error);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

            // Seed with a minimal, valid acquisition scaffold when created under acquis.d/,
            // otherwise start from an empty YAML document the user can fill in.
            var seed = segments[0].Equals("acquis.d", StringComparison.OrdinalIgnoreCase)
                ? "# CrowdSec acquisition file. Add one or more source blocks, e.g.:\n"
                  + "# filenames:\n"
                  + "#   - /var/log/nginx/access.log\n"
                  + "# labels:\n"
                  + "#   type: nginx\n"
                  + "#   override: /var/log/nginx/access.log\n"
                  : "";
            var temporaryPath = abs + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, seed);
                File.Move(temporaryPath, abs, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            _log.LogInformation("Created new CrowdSec config file {Path}", relativePath);
            error = null;
            return (true, null);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return (false, error);
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

    public Task BackupAsync(string targetDir, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var archive = Path.Combine(targetDir, $"crowdsec-config-{stamp}.tgz");
        var temporaryArchive = archive + ".tmp";
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            using (var output = File.Create(temporaryArchive))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
            using (var tar = new TarWriter(gzip, leaveOpen: false))
            {
                var pathComparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                var configRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_opt.ConfigDir));
                var backupRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir));
                if (string.Equals(configRoot, backupRootPath, pathComparison))
                    throw new InvalidOperationException("The backup target must not be the CrowdSec config directory itself.");
                var configRootPrefix = configRoot + Path.DirectorySeparatorChar;
                var backupRoot = backupRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
                var backupIsInsideConfig = backupRoot.StartsWith(configRootPrefix, pathComparison);
                foreach (var filePath in Directory.EnumerateFiles(_opt.ConfigDir, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    if (backupIsInsideConfig && Path.GetFullPath(filePath).StartsWith(backupRoot, pathComparison)) continue;
                    var entryName = Path.GetRelativePath(_opt.ConfigDir, filePath).Replace('\\', '/');
                    using var input = File.OpenRead(filePath);
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = input };
                    tar.WriteEntry(entry);
                }
            }

            File.Move(temporaryArchive, archive);
            _log.LogInformation("Backed up CrowdSec configuration to {Archive}", archive);
        }
        finally
        {
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
        }
        return Task.CompletedTask;
    }

    private static bool IsYamlPath(string path)
        => string.Equals(Path.GetExtension(path), ".yaml", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Path.GetExtension(path), ".yml", StringComparison.OrdinalIgnoreCase);

    private bool IsAllowed(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel)) return false;
        var normalized = rel.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or "..")) return false;
        if (!IsYamlPath(normalized)) return false;

        var inWhitelist = segments.Length == 1
            ? KnownFiles.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase))
            : EditableDirectories.Contains(segments[0], StringComparer.OrdinalIgnoreCase);
        if (!inWhitelist) return false;

        var root = Path.GetFullPath(_opt.ConfigDir);
        var target = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var relativeToRoot = Path.GetRelativePath(root, target);
        if (Path.IsPathRooted(relativeToRoot)
            || relativeToRoot == ".."
            || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;

        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        return true;
    }
}
