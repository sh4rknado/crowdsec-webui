using Microsoft.AspNetCore.Identity;

namespace CrowdSecManager.Areas.Identity.Data;

public sealed class ApplicationUser : IdentityUser
{
    /// <summary>UTC timestamp of last successful login.</summary>
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>Optional display name shown in the top bar.</summary>
    public string? DisplayName { get; set; }
}
