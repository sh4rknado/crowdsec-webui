using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using CrowdSecManager.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class HttpSmokeTests
{
    [Fact]
    public async Task HealthEndpointReturnsOkJson()
    {
        using var factory = new ManagerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
        Assert.True(payload.RootElement.TryGetProperty("ts", out _));
    }

    [Fact]
    public async Task UnauthenticatedProtectedRouteRedirectsToLoginAndLoginPageLoads()
    {
        using var factory = new ManagerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var protectedResponse = await client.GetAsync("/decisions");
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fdecisions", protectedResponse.Headers.Location!.PathAndQuery);

        using var loginResponse = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Contains("Sign in", await loginResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HttpLoginAndLogoutIssueAndClearIdentityCookieWithAntiforgery()
    {
        using var factory = new ManagerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var loginPage = await client.GetAsync("/login?returnUrl=%2Fdecisions");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginToken = ExtractAntiforgeryToken(loginHtml);
        using var invalid = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["ReturnUrl"] = "/decisions",
            ["Input.Username"] = "http-test-admin",
            ["Input.Password"] = "incorrect"
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Invalid username or password", await invalid.Content.ReadAsStringAsync());

        using var loginPageAgain = await client.GetAsync("/login?returnUrl=%2Fdecisions");
        var validToken = ExtractAntiforgeryToken(await loginPageAgain.Content.ReadAsStringAsync());
        using var valid = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = validToken,
            ["ReturnUrl"] = "/decisions",
            ["Input.Username"] = "http-test-admin",
            ["Input.Password"] = "Strong!Password2026"
        }));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal("/decisions", valid.Headers.Location!.OriginalString);
        Assert.Contains(valid.Headers.GetValues("Set-Cookie"), value =>
            value.Contains(".AspNetCore.Identity.Application", StringComparison.Ordinal));

        using var protectedResponse = await client.GetAsync("/decisions");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        Assert.Contains("Active decisions", await protectedResponse.Content.ReadAsStringAsync());

        using var logoutPage = await client.GetAsync("/logout");
        Assert.Equal(HttpStatusCode.OK, logoutPage.StatusCode);
        var logoutToken = ExtractAntiforgeryToken(await logoutPage.Content.ReadAsStringAsync());
        using var logout = await client.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/login", logout.Headers.Location!.OriginalString);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value =>
            value.Contains(".AspNetCore.Identity.Application", StringComparison.Ordinal)
            && (value.Contains("expires=", StringComparison.OrdinalIgnoreCase)
                || value.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));

        using var protectedAfterLogout = await client.GetAsync("/decisions");
        Assert.Equal(HttpStatusCode.Redirect, protectedAfterLogout.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fdecisions", protectedAfterLogout.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task LoginRejectsMissingAntiforgeryTokenAndExternalReturnUrl()
    {
        using var factory = new ManagerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var missingToken = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReturnUrl"] = "https://attacker.example/",
            ["Input.Username"] = "http-test-admin",
            ["Input.Password"] = "Strong!Password2026"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        using var loginPage = await client.GetAsync("/login?returnUrl=https%3A%2F%2Fattacker.example%2F");
        var token = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "https://attacker.example/",
            ["Input.Username"] = "http-test-admin",
            ["Input.Password"] = "Strong!Password2026"
        }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html,
            "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"");
        Assert.True(match.Success, "Razor Page should render an antiforgery request token.");
        return match.Groups[1].Value;
    }

    private sealed class ManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "CrowdSecManager.HttpTests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:Path"] = Path.Combine(_dataRoot, "app.db"),
                    ["DataProtection:KeysPath"] = Path.Combine(_dataRoot, "keys"),
                    ["ADMIN_USER"] = "http-test-admin",
                    ["ADMIN_PASSWORD"] = "Strong!Password2026",
                    ["CrowdSec:ConfigDir"] = Path.Combine(_dataRoot, "crowdsec-config")
                }));

            builder.ConfigureTestServices(services =>
            {
                var threatFeed = services.FirstOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(ThreatFeedService));
                if (threatFeed is not null) services.Remove(threatFeed);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
