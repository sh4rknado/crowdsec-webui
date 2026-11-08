using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class NotificationsViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly IConfigFileService _config;
    private readonly ILogger<NotificationsViewModel> _log;
    private IReadOnlyList<CsNotification> _notifications = Array.Empty<CsNotification>();
    private bool _isBusy;
    private string? _message;
    private bool _hasError;
    private string? _inspectOutput;
    private string? _inspectName;
    private string _type = "slack";
    private string _name = "slack_default";
    private string _lastDefaultName = "slack_default";
    private string _scope = "all";
    private bool _enabled = true;
    private string _webhook = "";
    private string _url = "";
    private string _smtpHost = "";
    private string _smtpPort = "587";
    private string _senderEmail = "";
    private string _recipients = "";
    private string _smtpUser = "";
    private string _smtpPassword = "";
    private string _encryption = "starttls";

    public NotificationsViewModel(ICrowdSecCliService cli, IConfigFileService config, ILogger<NotificationsViewModel> log)
    {
        _cli = cli;
        _config = config;
        _log = log;
    }

    public IReadOnlyList<CsNotification> Notifications { get => _notifications; private set => SetProperty(ref _notifications, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string? InspectOutput { get => _inspectOutput; private set => SetProperty(ref _inspectOutput, value); }
    public string? InspectName { get => _inspectName; private set => SetProperty(ref _inspectName, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Scope { get => _scope; set => SetProperty(ref _scope, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Webhook { get => _webhook; set => SetProperty(ref _webhook, value); }
    public string Url { get => _url; set => SetProperty(ref _url, value); }
    public string SmtpHost { get => _smtpHost; set => SetProperty(ref _smtpHost, value); }
    public string SmtpPort { get => _smtpPort; set => SetProperty(ref _smtpPort, value); }
    public string SenderEmail { get => _senderEmail; set => SetProperty(ref _senderEmail, value); }
    public string Recipients { get => _recipients; set => SetProperty(ref _recipients, value); }
    public string SmtpUser { get => _smtpUser; set => SetProperty(ref _smtpUser, value); }
    public string SmtpPassword { get => _smtpPassword; set => SetProperty(ref _smtpPassword, value); }
    public string Encryption { get => _encryption; set => SetProperty(ref _encryption, value); }

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
