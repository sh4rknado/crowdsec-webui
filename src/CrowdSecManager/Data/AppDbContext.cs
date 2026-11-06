using CrowdSecManager.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrowdSecManager.Data;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ThreatHistoryEntry> ThreatHistory => Set<ThreatHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ThreatHistoryEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.SourceIp);
            e.Property(x => x.SourceIp).HasMaxLength(64);
            e.Property(x => x.Scenario).HasMaxLength(256);
            e.Property(x => x.CountryIso).HasMaxLength(2);
        });
    }
}
