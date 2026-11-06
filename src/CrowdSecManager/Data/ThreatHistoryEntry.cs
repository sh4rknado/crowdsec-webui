using CrowdSecManager.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrowdSecManager.Data;

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
