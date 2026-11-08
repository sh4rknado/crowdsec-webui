namespace CrowdsecManagerModel;

/// <summary>Notification plugin returned by <c>cscli notifications list -o json</c>.</summary>
public sealed class CsNotification
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? ConfigPath { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
}
