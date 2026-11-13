using CrowdSecManager.Services;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class AppBackupServiceTests
{
    [Fact]
    public async Task BackupAndRestore_RestoresYamlEngineDataManagerFilesAndSqliteDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"app-backup-test-{Guid.NewGuid():N}");
        var configDir = Path.Combine(root, "config");
        var dataDir = Path.Combine(root, "engine-data");
        var managerDir = Path.Combine(root, "manager");
        var dbPath = Path.Combine(managerDir, "app.db");
        Directory.CreateDirectory(Path.Combine(configDir, "notifications"));
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(managerDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(configDir, "config.yaml"), "db_path: /var/lib/crowdsec/data/crowdsec.db\n");
            await File.WriteAllTextAsync(Path.Combine(configDir, "notifications", "http.yaml"), "url: https://example.test\n");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "hub-index.json"), "original-engine-data");
            await File.WriteAllTextAsync(Path.Combine(managerDir, "crowdsec-manager-bouncer.key"), "original-key");
            await CreateDatabaseAsync(dbPath, "original-user");

            var service = CreateService(configDir, dataDir, dbPath);
            await using var archive = new MemoryStream();
            await service.WriteBackupAsync(archive);
            archive.Position = 0;
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
            {
                var dbEntry = zip.GetEntry("manager-data/app.db");
                Assert.NotNull(dbEntry);
                var snapshotDb = Path.Combine(root, "snapshot.db");
                await using (var entryStream = dbEntry.Open())
                await using (var snapshotFile = File.Create(snapshotDb))
                    await entryStream.CopyToAsync(snapshotFile);
                Assert.Equal("original-user", await ReadDatabaseValueAsync(snapshotDb));
            }

            await File.WriteAllTextAsync(Path.Combine(configDir, "config.yaml"), "changed: true\n");
            await File.WriteAllTextAsync(Path.Combine(dataDir, "hub-index.json"), "changed-engine-data");
            await File.WriteAllTextAsync(Path.Combine(managerDir, "crowdsec-manager-bouncer.key"), "changed-key");
            await UpdateDatabaseAsync(dbPath, "changed-user");

            archive.Position = 0;
            await service.RestoreAsync(archive);

            Assert.Contains("db_path:", await File.ReadAllTextAsync(Path.Combine(configDir, "config.yaml")));
            Assert.Equal("url: https://example.test\n", await File.ReadAllTextAsync(Path.Combine(configDir, "notifications", "http.yaml")));
            Assert.Equal("original-engine-data", await File.ReadAllTextAsync(Path.Combine(dataDir, "hub-index.json")));
            Assert.Equal("original-key", await File.ReadAllTextAsync(Path.Combine(managerDir, "crowdsec-manager-bouncer.key")));
            Assert.Equal("original-user", await ReadDatabaseValueAsync(dbPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static IAppBackupService CreateService(string configDir, string dataDir, string dbPath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrowdSec:ConfigDir"] = configDir,
            ["CrowdSec:DataDir"] = dataDir,
            ["Database:Path"] = dbPath
        }).Build();
        return new AppBackupService(configuration, NullLogger<AppBackupService>.Instance);
    }

    private static async Task CreateDatabaseAsync(string path, string value)
    {
        await using var db = new SqliteConnection($"Data Source={path}");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE state (value TEXT NOT NULL); INSERT INTO state VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateDatabaseAsync(string path, string value)
    {
        await using var db = new SqliteConnection($"Data Source={path}");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "UPDATE state SET value = $value;";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadDatabaseValueAsync(string path)
    {
        await using var db = new SqliteConnection($"Data Source={path}");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT value FROM state;";
        return (string?)await command.ExecuteScalarAsync();
    }
}
