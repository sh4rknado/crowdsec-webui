using CrowdSecManager.Areas.Identity.Data;
using CrowdSecManager.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class IdentityAndPersistenceTests
{
    [Fact]
    public async Task AppDatabase_PersistsThreatHistoryInSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.ThreatHistory.Add(new ThreatHistoryEntry
            {
                AlertId = 87,
                CreatedAt = DateTime.UtcNow,
                SourceIp = "203.0.113.15",
                Scenario = "crowdsecurity/ssh-bf",
                Severity = "High"
            });
            await db.SaveChangesAsync();
        }

        await using var reopened = new AppDbContext(options);
        var saved = await reopened.ThreatHistory.SingleAsync();
        Assert.Equal(87, saved.AlertId);
        Assert.Equal("203.0.113.15", saved.SourceIp);
        Assert.Equal("High", saved.Severity);
    }

    [Fact]
    public async Task DbSeeder_CreatesAdministratorAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_USER"] = "operator",
                ["ADMIN_PASSWORD"] = "Strong!Password2026"
            })
            .Build());
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            await DbSeeder.SeedAsync(scope.ServiceProvider);
            await DbSeeder.SeedAsync(scope.ServiceProvider);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync("operator");
            Assert.NotNull(user);
            Assert.True(await users.CheckPasswordAsync(user!, "Strong!Password2026"));
            Assert.True(await users.IsInRoleAsync(user!, "Administrator"));
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.Roles.CountAsync());
        }
    }
}
