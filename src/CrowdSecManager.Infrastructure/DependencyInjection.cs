using CrowdSecManager.Data;
using CrowdSecManager.Core.Repositories;
using CrowdSecManager.Infrastructure.Repositories;
using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CrowdSecManager.Infrastructure;

/// <summary>Registers infrastructure adapters and persistence for the web host.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrowdSecOptions>(configuration.GetSection("CrowdSec"));
        services.Configure<GeoIpOptions>(configuration.GetSection("GeoIp"));

        var dbPath = configuration["Database:Path"] ?? "/var/lib/crowdsec-manager/app.db";
        var keyDirectory = configuration["DataProtection:KeysPath"]
                           ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "keys");
        Directory.CreateDirectory(keyDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

        try
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
        catch
        {
            // Container volumes can be mounted after configuration is read.
        }

        services.AddHttpClient<ICrowdSecApiService, CrowdSecApiService>();
        services.AddSingleton<ICrowdSecProcessRunner, CrowdSecProcessRunner>();
        services.AddSingleton<ICrowdSecCliService, CrowdSecCliService>();
        services.AddSingleton<ITerminalRepository, CrowdSecCliRepository>();
        services.AddSingleton<IGeoIpService, GeoIpService>();
        services.AddSingleton<IThreatMapEventBus, ThreatMapEventBus>();
        services.AddScoped<IConfigFileService, ConfigFileService>();
        services.AddHostedService<ThreatFeedService>();
        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        try
        {
            var db = provider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            await DbSeeder.SeedAsync(provider);
        }
        catch (Exception ex)
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap");
            logger.LogCritical(ex, "DB bootstrap or administrator seeding failed; refusing to start without a usable identity store.");
            throw;
        }
    }
}
