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
