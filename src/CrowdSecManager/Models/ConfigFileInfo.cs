namespace CrowdSecManager.Models;

public sealed class ConfigFileInfo
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public string Description { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Content { get; set; } = "";
}
