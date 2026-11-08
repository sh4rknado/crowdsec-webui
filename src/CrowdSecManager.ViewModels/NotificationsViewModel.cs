using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class NotificationsViewModel(ICrowdSecCliService cli, IConfigFileService config, ILogger<NotificationsViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));
    private readonly IConfigFileService _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<NotificationsViewModel> _log = log ?? throw new ArgumentNullException(nameof(log));
    private string _lastDefaultName = "slack_default";

    public IReadOnlyList<CsNotification> Notifications { get; private set => SetProperty(ref field, value); } = Array.Empty<CsNotification>();
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string? InspectOutput { get; private set => SetProperty(ref field, value); }
    public string? InspectName { get; private set => SetProperty(ref field, value); }
    public string Type { get; private set => SetProperty(ref field, value); } = "slack";
    public string Name { get; private set => SetProperty(ref field, value); } = "slack_default";
    public string Scope { get; private set => SetProperty(ref field, value); } = "all";
    public bool Enabled { get; private set => SetProperty(ref field, value); } = true;
    public string Webhook { get; private set => SetProperty(ref field, value); } = "";
    public string Url { get; private set => SetProperty(ref field, value); } = "";
    public string SmtpHost { get; private set => SetProperty(ref field, value); } = "";
    public string SmtpPort { get; private set => SetProperty(ref field, value); } = "587";
    public string SenderEmail { get; private set => SetProperty(ref field, value); } = "";
    public string Recipients { get; private set => SetProperty(ref field, value); } = "";
    public string SmtpUser { get; private set => SetProperty(ref field, value); } = "";
    public string SmtpPassword { get; private set => SetProperty(ref field, value); } = "";
    public string Encryption { get; private set => SetProperty(ref field, value); } = "starttls";

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            Notifications = await _cli.ListNotificationsAsync();
        }
        catch (Exception ex)
        {
            _log.LogDebug("notifications: {Msg}", ex.Message);
        }
    }

    public Task UpdateDefaultNameAsync()
    {
        if (Name == _lastDefaultName)
            Name = DefaultPluginName(Type);

        _lastDefaultName = DefaultPluginName(Type);
        return Task.CompletedTask;
    }

    public async Task SaveNotificationAsync()
    {
        IsBusy = true;
        Message = null;
        HasError = false;

        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["webhook"] = Webhook,
                ["url"] = Url,
                ["smtp_host"] = SmtpHost,
                ["smtp_port"] = SmtpPort,
                ["sender_email"] = SenderEmail,
                ["receiver_emails"] = Recipients,
                ["smtp_username"] = SmtpUser,
                ["smtp_password"] = SmtpPassword,
                ["encryption_type"] = Encryption
            };
            var plugin = NotificationConfigBuilder.BuildPlugin(Type, Name, settings);
            if (!plugin.Valid)
            {
                HasError = true;
                Message = plugin.Error;
                return;
            }

            var existingProfile = _config.Load("profiles.yaml.local")?.Content;
            if (existingProfile is null)
            {
                HasError = true;
                Message = "Could not read profiles.yaml.local.";
                return;
            }

            var profile = NotificationConfigBuilder.SetProfile(existingProfile, Name, Scope, Enabled);
            if (!profile.Valid)
            {
                HasError = true;
                Message = profile.Error;
                return;
            }

            var pluginSave = _config.Save(plugin.FileName!, plugin.Yaml!);
            if (!pluginSave.Valid)
            {
                HasError = true;
                Message = pluginSave.Error;
                return;
            }

            var profileSave = _config.Save("profiles.yaml.local", profile.Content!);
            if (!profileSave.Valid)
            {
                HasError = true;
                Message = profileSave.Error;
                return;
            }

            var target = Scope == "all" ? "all remediation alerts" : $"{Scope} decisions";
            Message = Enabled
                ? $"Saved {Name} and enabled it for {target}. Reload CrowdSec to apply it."
                : $"Saved {Name} and disabled its managed profile. Reload CrowdSec to apply it.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save notification configuration");
            HasError = true;
            Message = "Could not save the notification configuration.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InspectAsync(string name)
    {
        IsBusy = true;
        Message = null;

        try
        {
            var result = await _cli.InspectNotificationAsync(name);
            HasError = !result.Success;
            InspectName = name;
            InspectOutput = result.Success
                ? result.Stdout
                : $"Exit code {result.ExitCode}\n\n{result.FlattenError() ?? result.Stderr}";
            if (!result.Success)
                Message = $"Failed to inspect {name}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static string? Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? value : value.Length > length ? value[..length] + "..." : value;

    private static string DefaultPluginName(string type) => type switch
    {
        "http" => "http_default",
        "email" => "email_default",
        _ => "slack_default"
    };
}
