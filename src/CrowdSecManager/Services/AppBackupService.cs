using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace CrowdSecManager.Services;

/// <summary>Creates and restores a portable snapshot of CrowdSec and manager state.</summary>
public sealed class AppBackupService(IConfiguration configuration, ILogger<AppBackupService> logger) : IAppBackupService
{
    private const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _configDir = Path.GetFullPath(configuration["CrowdSec:ConfigDir"] ?? "/etc/crowdsec");
    private readonly string _dataDir = Path.GetFullPath(configuration["CrowdSec:DataDir"] ?? "/var/lib/crowdsec/data");
    private readonly string _databasePath = Path.GetFullPath(configuration["Database:Path"] ?? "/var/lib/crowdsec-manager/app.db");
    private readonly ILogger<AppBackupService> _logger = logger;

    public async Task WriteBackupAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var tempDb = Path.Combine(Path.GetTempPath(), $"crowdsec-manager-backup-{Guid.NewGuid():N}.db");
        try
        {
            if (File.Exists(_databasePath))
                await SnapshotSqliteAsync(_databasePath, tempDb, cancellationToken);

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            AddDirectory(archive, _configDir, "crowdsec-config", cancellationToken);
            AddDirectory(archive, _dataDir, "crowdsec-data", cancellationToken);

            var managerDir = Path.GetDirectoryName(_databasePath)!;
            AddDirectory(archive, managerDir, "manager-data", cancellationToken,
                path => IsSqliteFile(path, _databasePath));

            if (File.Exists(tempDb))
                AddFile(archive, tempDb, "manager-data/app.db", cancellationToken);

            var manifest = archive.CreateEntry("backup-manifest.txt", CompressionLevel.Fastest);
            await using var writer = new StreamWriter(manifest.Open());
            await writer.WriteAsync("CrowdSec Manager full backup v1\n" + DateTimeOffset.UtcNow.ToString("O") + "\n");
        }
        finally
        {
            TryDelete(tempDb);
        }
    }

    public async Task RestoreAsync(Stream archiveStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        var tempArchive = Path.Combine(Path.GetTempPath(), $"crowdsec-manager-restore-{Guid.NewGuid():N}.zip");
        var staging = Path.Combine(Path.GetTempPath(), $"crowdsec-manager-restore-{Guid.NewGuid():N}");
        try
        {
            await using (var output = new FileStream(tempArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await CopyLimitedAsync(archiveStream, output, MaxArchiveBytes, cancellationToken);
            }

            Directory.CreateDirectory(staging);
            string? restoredDb = null;
            using (var archive = ZipFile.OpenRead(tempArchive))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/')) continue;
                    var relative = ValidateEntryName(entry.FullName);
                    var target = Path.GetFullPath(Path.Combine(staging, relative));
                    EnsureInside(staging, target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var source = entry.Open();
                    await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    await CopyLimitedAsync(source, file, MaxArchiveBytes, cancellationToken);
                    if (entry.FullName.Equals("manager-data/app.db", StringComparison.Ordinal)) restoredDb = target;
                }
            }

            RestoreTree(staging, "crowdsec-config", _configDir, cancellationToken);
            RestoreTree(staging, "crowdsec-data", _dataDir, cancellationToken);
            RestoreTree(staging, "manager-data", Path.GetDirectoryName(_databasePath)!, cancellationToken,
                relative => relative is "app.db" or "app.db-wal" or "app.db-shm");

            if (restoredDb is not null)
                await RestoreSqliteAsync(restoredDb, _databasePath, cancellationToken);

            _logger.LogWarning("Application backup restored. Existing files with matching paths were replaced; additional local files were retained.");
        }
        finally
        {
            TryDelete(tempArchive);
            TryDeleteDirectory(staging);
        }
    }

    private static async Task SnapshotSqliteAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString());
        await source.OpenAsync(ct);
        await destination.OpenAsync(ct);
        source.BackupDatabase(destination);
    }

    private static async Task RestoreSqliteAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString());
        await source.OpenAsync(ct);
        await destination.OpenAsync(ct);
        source.BackupDatabase(destination);
    }

    private static void AddDirectory(ZipArchive archive, string sourceDir, string archiveRoot, CancellationToken ct, Func<string, bool>? exclude = null)
    {
        if (!Directory.Exists(sourceDir)) return;
        foreach (var path in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (exclude?.Invoke(path) == true) continue;
            var relative = Path.GetRelativePath(sourceDir, path).Replace('\\', '/');
            AddFile(archive, path, $"{archiveRoot}/{relative}", ct);
        }
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryName, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = entry.Open();
        input.CopyTo(output);
        ct.ThrowIfCancellationRequested();
    }

    private static bool IsSqliteFile(string path, string databasePath)
        => string.Equals(Path.GetFullPath(path), databasePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
           || string.Equals(path, databasePath + "-wal", StringComparison.Ordinal)
           || string.Equals(path, databasePath + "-shm", StringComparison.Ordinal);

    private static void RestoreTree(string staging, string archiveRoot, string targetRoot, CancellationToken ct, Func<string, bool>? exclude = null)
    {
        var sourceRoot = Path.Combine(staging, archiveRoot);
        if (!Directory.Exists(sourceRoot)) return;
        Directory.CreateDirectory(targetRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (exclude?.Invoke(relative) == true) continue;
            var target = Path.GetFullPath(Path.Combine(targetRoot, relative));
            EnsureInside(targetRoot, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static string ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains('\\'))
            throw new InvalidDataException("The backup contains an invalid path.");
        var parts = name.Split('/');
        if (parts.Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("The backup contains an unsafe path.");
        if (parts[0] is not ("crowdsec-config" or "crowdsec-data" or "manager-data" or "backup-manifest.txt"))
            throw new InvalidDataException("The backup contains an unknown data section.");
        return Path.Combine(parts);
    }

    private static void EnsureInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The backup contains a path outside its data directory.");
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
        {
            total += read;
            if (total > maxBytes) throw new InvalidDataException("The backup exceeds the 2 GiB size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
