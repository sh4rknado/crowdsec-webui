using CrowdSecManager.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrowdSecManager.Data;

/// <summary>Idempotently seeds an <c>admin</c> role + a default admin user
/// (configured via env vars <c>ADMIN_USER</c> / <c>ADMIN_PASSWORD</c>).</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var cfg = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        const string AdminRole = "Administrator";
        if (!await roleMgr.RoleExistsAsync(AdminRole))
            await roleMgr.CreateAsync(new IdentityRole(AdminRole));

        var adminUser = cfg["ADMIN_USER"] ?? "admin";
        var existing = await userMgr.FindByNameAsync(adminUser);
        if (existing is not null) return;

        var password = cfg["ADMIN_PASSWORD"]
                       ?? throw new InvalidOperationException(
                           "ADMIN_PASSWORD must be set on first boot to seed the admin user.");

        var user = new ApplicationUser
        {
            UserName = adminUser,
            Email = adminUser,
            EmailConfirmed = true,
            DisplayName = "CrowdSec Manager Administrator",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var create = await userMgr.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            var errors = string.Join(", ", create.Errors.Select(e => e.Description));
            logger.LogError("Could not seed admin user: {Errors}", errors);
            return;
        }

        await userMgr.AddToRoleAsync(user, AdminRole);
        logger.LogInformation("Seeded admin user '{User}' with Administrator role.", adminUser);
    }
}
