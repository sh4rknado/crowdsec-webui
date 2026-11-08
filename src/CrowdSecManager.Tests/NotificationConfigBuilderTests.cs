using CrowdSecManager.Services;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class NotificationConfigBuilderTests
{
    [Theory]
    [InlineData("slack", "slack_default", "webhook: https://hooks.example.test/x")]
    [InlineData("http", "http_default", "url: https://hooks.example.test/crowdsec")]
    public void BuildPlugin_CreatesValidYamlWithMatchingTypeAndName(string type, string name, string setting)
    {
        var pair = setting.Split(": ", 2);
        var result = NotificationConfigBuilder.BuildPlugin(type, name,
            new Dictionary<string, string?> { [pair[0]] = pair[1] });

        Assert.True(result.Valid, result.Error);
        Assert.Equal($"notifications/{name}.yaml", result.FileName);
        var document = LoadSingle(result.Yaml!);
        Assert.Equal(type, Scalar(document, "type"));
        Assert.Equal(name, Scalar(document, "name"));
    }

    [Fact]
    public void BuildPlugin_EmailIncludesRecipientsAndPasswordAsYamlValues()
    {
        var result = NotificationConfigBuilder.BuildPlugin("email", "email_default", new Dictionary<string, string?>
        {
            ["smtp_host"] = "smtp.example.test", ["smtp_port"] = "587",
            ["sender_email"] = "crowdsec@example.test", ["receiver_emails"] = "sec@example.test, ops@example.test",
            ["smtp_username"] = "mailer",
            ["smtp_password"] = "p@ss: word"
        });

        Assert.True(result.Valid, result.Error);
        var mapping = LoadSingle(result.Yaml!);
        Assert.Equal("p@ss: word", Scalar(mapping, "smtp_password"));
        Assert.Equal(2, ((YamlSequenceNode)mapping.Children[new YamlScalarNode("receiver_emails")]).Children.Count);
    }

    [Theory]
    [InlineData("unknown", "plugin", "https://example.test")]
    [InlineData("slack", "../bad", "https://example.test")]
    [InlineData("http", "http_default", "javascript:alert(1)")]
    public void BuildPlugin_RejectsUnsupportedOrUnsafeConfiguration(string type, string name, string value)
    {
        var settings = type == "slack" ? new Dictionary<string, string?> { ["webhook"] = value }
            : new Dictionary<string, string?> { ["url"] = value, ["webhook"] = value };

        var result = NotificationConfigBuilder.BuildPlugin(type, name, settings);

        Assert.False(result.Valid);
        Assert.Null(result.Yaml);
    }

    [Fact]
    public void BuildPlugin_EmailRequiresMatchingCredentialsAndSupportedEncryption()
    {
        var settings = new Dictionary<string, string?>
        {
            ["smtp_host"] = "smtp.example.test", ["smtp_port"] = "587",
            ["sender_email"] = "crowdsec@example.test", ["receiver_emails"] = "sec@example.test",
            ["smtp_username"] = "mailer", ["encryption_type"] = "invalid"
        };

        var result = NotificationConfigBuilder.BuildPlugin("email", "email_default", settings);

        Assert.False(result.Valid);
        Assert.Contains("both the SMTP username and password", result.Error);
    }

    [Theory]
    [InlineData("all", "Alert.Remediation == true")]
    [InlineData("Ip", "Alert.GetScope() == \"Ip\"")]
    [InlineData("Range", "Alert.GetScope() == \"Range\"")]
    public void SetProfile_AddsManagedNotificationProfileAndContinues(string scope, string expectedFilter)
    {
        var result = NotificationConfigBuilder.SetProfile("name: existing\nfilters:\n  - 1 == 1\non_success: continue\n", "slack_default", scope, true);

        Assert.True(result.Valid, result.Error);
        Assert.Contains("name: existing", result.Content);
        Assert.Contains("notifications:\n  - slack_default", result.Content);
        Assert.Contains(expectedFilter, result.Content);
        Assert.Contains("on_success: continue", result.Content);
        Assert.Equal(2, LoadAll(result.Content!).Documents.Count);
    }

    [Fact]
    public void SetProfile_IsIdempotentAndCanDisableOnlyItsManagedDocument()
    {
        var first = NotificationConfigBuilder.SetProfile("", "slack_default", "all", true);
        var second = NotificationConfigBuilder.SetProfile(first.Content, "slack_default", "Ip", true);
        var disabled = NotificationConfigBuilder.SetProfile(second.Content, "slack_default", "Ip", false);

        Assert.True(first.Valid && second.Valid && disabled.Valid);
        Assert.Single(LoadAll(second.Content!).Documents);
        Assert.Contains("Alert.GetScope()", second.Content);
        Assert.Equal("", disabled.Content);
    }

    [Fact]
    public void SetProfile_PreservesOtherLocalProfilesAndRejectsInvalidYaml()
    {
        const string existing = "---\nname: other\nfilters:\n  - 1 == 1\non_success: continue\n";
        var added = NotificationConfigBuilder.SetProfile(existing, "http_default", "all", true);
        var invalid = NotificationConfigBuilder.SetProfile("name: [broken\n", "http_default", "all", true);

        Assert.True(added.Valid, added.Error);
        Assert.Equal(2, LoadAll(added.Content!).Documents.Count);
        Assert.False(invalid.Valid);
    }

    private static YamlMappingNode LoadSingle(string yaml)
    {
        var stream = LoadAll(yaml);
        return Assert.IsType<YamlMappingNode>(Assert.Single(stream.Documents).RootNode);
    }

    private static YamlStream LoadAll(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream;
    }

    private static string Scalar(YamlMappingNode node, string key)
        => ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value!;
}
