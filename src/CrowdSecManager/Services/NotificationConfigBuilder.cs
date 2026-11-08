using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace CrowdSecManager.Services;

/// <summary>Builds CrowdSec notification plugin YAML and a managed local profile.</summary>
public static partial class NotificationConfigBuilder
{
    private const string MarkerPrefix = "# crowdsec-manager-notification:";

    public static (bool Valid, string? Error, string? FileName, string? Yaml) BuildPlugin(
        string type, string name, IReadOnlyDictionary<string, string?> settings)
    {
        type = (type ?? "").Trim().ToLowerInvariant();
        name = (name ?? "").Trim();
        if (type is not ("slack" or "http" or "email"))
            return (false, "Choose Slack, HTTP webhook, or email.", null, null);
        if (!PluginNameRegex().IsMatch(name))
            return (false, "Plugin name may contain only letters, numbers, hyphens, and underscores.", null, null);

        var document = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["name"] = name,
            ["log_level"] = "info"
        };
        switch (type)
        {
            case "slack":
                if (!Required(settings, "webhook", out var webhook, out var error))
                    return (false, error, null, null);
                if (!Uri.TryCreate(webhook, UriKind.Absolute, out var slackUri) || slackUri.Scheme != "https")
                    return (false, "Slack webhook must be an absolute HTTPS URL.", null, null);
                document["format"] = "{{range .}}{{range .Decisions}}{{.Value}} will get {{.Type}} for {{.Duration}} ({{.Scenario}}){{end}}{{end}}";
                document["webhook"] = webhook;
                break;
            case "http":
                if (!Required(settings, "url", out var url, out error))
                    return (false, error, null, null);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    return (false, "Webhook URL must be an absolute HTTP or HTTPS URL.", null, null);
                document["format"] = "{{.|toJson}}";
                document["url"] = url;
                document["method"] = "POST";
                break;
            case "email":
                foreach (var key in new[] { "smtp_host", "smtp_port", "sender_email", "receiver_emails" })
                    if (!Required(settings, key, out _, out error))
                        return (false, error, null, null);
                if (!int.TryParse(settings["smtp_port"], out var port) || port is < 1 or > 65535)
                    return (false, "SMTP port must be between 1 and 65535.", null, null);
                var recipients = settings["receiver_emails"]!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (recipients.Length == 0)
                    return (false, "Enter at least one recipient email address.", null, null);
                var smtpUsername = settings.GetValueOrDefault("smtp_username")?.Trim();
                var smtpPassword = settings.GetValueOrDefault("smtp_password");
                if (string.IsNullOrEmpty(smtpUsername) != string.IsNullOrEmpty(smtpPassword))
                    return (false, "Enter both the SMTP username and password, or leave both empty.", null, null);
                var encryption = string.IsNullOrWhiteSpace(settings.GetValueOrDefault("encryption_type")) ? "starttls" : settings["encryption_type"]!.Trim().ToLowerInvariant();
                if (encryption is not ("starttls" or "ssltls" or "none"))
                    return (false, "Encryption must be STARTTLS, SSL/TLS, or none.", null, null);
                document["timeout"] = "20s";
                document["format"] = "CrowdSec detected an alert: {{range .}}{{range .Decisions}}{{.Value}} will get {{.Type}} for {{.Duration}} ({{.Scenario}}). {{end}}{{end}}";
                document["smtp_host"] = settings["smtp_host"]!.Trim();
                document["smtp_port"] = port;
                document["sender_name"] = string.IsNullOrWhiteSpace(settings.GetValueOrDefault("sender_name")) ? "CrowdSec" : settings["sender_name"]!.Trim();
                document["email_subject"] = "CrowdSec Notification";
                document["sender_email"] = settings["sender_email"]!.Trim();
                document["receiver_emails"] = recipients;
                document["encryption_type"] = encryption;
                document["auth_type"] = string.IsNullOrEmpty(smtpUsername) ? "none" : "login";
                if (!string.IsNullOrEmpty(smtpUsername))
                    document["smtp_username"] = smtpUsername;
                if (!string.IsNullOrEmpty(smtpPassword))
                    document["smtp_password"] = smtpPassword;
                break;
        }

        var yaml = new SerializerBuilder().Build().Serialize(document);
        return (true, null, $"notifications/{name}.yaml", yaml);
    }

    public static (bool Valid, string? Error, string? Content) SetProfile(
        string? existing, string pluginName, string scope, bool enabled)
    {
        if (!PluginNameRegex().IsMatch(pluginName ?? ""))
            return (false, "Invalid plugin name.", null);
        if (scope is not ("all" or "Ip" or "Range"))
            return (false, "Choose all alerts, IP decisions, or range decisions.", null);

        var content = existing ?? "";
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var stream = new YamlStream();
                using var reader = new StringReader(content);
                stream.Load(reader);
            }
            catch (Exception ex) { return (false, $"Existing profiles.yaml.local is invalid YAML: {ex.Message}", null); }
        }

        var marker = MarkerPrefix + pluginName;
        var managed = ManagedProfileRegex(Regex.Escape(marker));
        content = managed.Replace(content, "").TrimEnd();
        if (enabled)
        {
            var filter = scope switch
            {
                "Ip" => "Alert.Remediation == true && Alert.GetScope() == \"Ip\"",
                "Range" => "Alert.Remediation == true && Alert.GetScope() == \"Range\"",
                _ => "Alert.Remediation == true"
            };
            var profile = $"---\n{marker}\nname: manager_notification_{pluginName}\nfilters:\n  - '{filter}'\nnotifications:\n  - {pluginName}\non_success: continue\n";
            content = string.IsNullOrWhiteSpace(content) ? profile : content + "\n" + profile;
        }
        return (true, null, content.Length == 0 ? "" : content.TrimEnd() + "\n");
    }

    private static bool Required(IReadOnlyDictionary<string, string?> settings, string key, out string value, out string error)
    {
        value = settings.GetValueOrDefault(key)?.Trim() ?? "";
        if (value.Length > 0) { error = ""; return true; }
        error = $"{key.Replace('_', ' ')} is required.";
        return false;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginNameRegex();

    private static Regex ManagedProfileRegex(string marker)
        => new($"(?ms)^---\\r?\\n{marker}\\r?\\n.*?(?=^---\\s*$|\\z)", RegexOptions.CultureInvariant);
}
