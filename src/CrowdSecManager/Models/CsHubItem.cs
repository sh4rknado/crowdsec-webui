namespace CrowdSecManager.Models;

/// <summary>Hub item as returned by <c>cscli hub list -o json</c>.</summary>
public sealed class CsHubItem
{
    public string? Name { get; set; }
    public string? LocalVersion { get; set; }
    public string? LocalPath { get; set; }
    public string? Description { get; set; }
    public string? LatestVersion { get; set; }
    public string? Type { get; set; }
    public string? LocalHash { get; set; }
    public string? LocalHubItem { get; set; }
    public string? Status { get; set; }
    public bool Tainted { get; set; }
    public bool Installed { get; set; }
    public bool UpToDate { get; set; }
}
