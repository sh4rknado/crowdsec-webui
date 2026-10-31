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

/// <summary>Persisted threat log entry - keeps a 30-day rolling window for trend charts.</summary>
public sealed class ThreatHistoryEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public long AlertId { get; set; }
    public string? SourceIp { get; set; }
    public string? CountryIso { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Scenario { get; set; }
    public string? Message { get; set; }
    public string? Severity { get; set; }
}
