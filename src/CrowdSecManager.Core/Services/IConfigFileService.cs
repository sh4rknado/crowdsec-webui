using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

public interface IConfigFileService
{
    IReadOnlyList<ConfigFileInfo> ListConfigFiles();
    ConfigFileInfo? Load(string relativePath);
    (bool Valid, string? Error) Save(string relativePath, string content);
    (bool Valid, string? Error) ValidateYaml(string content);
    Task BackupAsync(string targetDir, CancellationToken ct = default);
    (bool Created, string? Error) TryCreate(string relativePath, out string? error);
}
