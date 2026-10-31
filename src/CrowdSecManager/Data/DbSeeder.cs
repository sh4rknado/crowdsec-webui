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
        {
            var createRole = await roleMgr.CreateAsync(new IdentityRole(AdminRole));
            if (!createRole.Succeeded)
            {
                var errors = string.Join(", ", createRole.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Could not create the Administrator role: {errors}");
            }
        }

        var adminUser = cfg["ADMIN_USER"] ?? "admin";
        var existing = await userMgr.FindByNameAsync(adminUser);
        if (existing is null)
        {
            var password = cfg["ADMIN_PASSWORD"]
                           ?? throw new InvalidOperationException(
                               "ADMIN_PASSWORD must be set on first boot to seed the admin user.");

            existing = new ApplicationUser
            {
                UserName = adminUser,
                Email = adminUser,
                EmailConfirmed = true,
                DisplayName = "CrowdSec Manager Administrator",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };

            var createUser = await userMgr.CreateAsync(existing, password);
            if (!createUser.Succeeded)
            {
                var errors = string.Join(", ", createUser.Errors.Select(e => e.Description));
                logger.LogError("Could not seed admin user: {Errors}", errors);
                throw new InvalidOperationException($"Could not seed admin user: {errors}");
            }
        }

        if (!await userMgr.IsInRoleAsync(existing, AdminRole))
        {
            var addRole = await userMgr.AddToRoleAsync(existing, AdminRole);
            if (!addRole.Succeeded)
            {
                var errors = string.Join(", ", addRole.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Could not assign Administrator role to '{adminUser}': {errors}");
            }
        }

        logger.LogInformation("Seeded administrator user '{User}'.", adminUser);
    }
}
