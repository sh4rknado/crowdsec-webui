using System.Net;
using System.Text.Json;
using CrowdSecManager.Areas.Identity.Data;
using CrowdSecManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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
    public async Task IdentityCookieSignInAndSignOutWorkWithSeededAdministrator()
    {
        using var factory = new ManagerFactory();
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        var invalid = await signIn.PasswordSignInAsync(
            "http-test-admin", "incorrect", isPersistent: true, lockoutOnFailure: true);
        Assert.False(invalid.Succeeded);

        var valid = await signIn.PasswordSignInAsync(
            "http-test-admin", "Strong!Password2026", isPersistent: true, lockoutOnFailure: true);
        Assert.True(valid.Succeeded);
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(".AspNetCore.Identity.Application", cookie);

        using (var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            using var protectedResponse = await client.GetAsync("/decisions");
            Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
            Assert.Contains("Active decisions", await protectedResponse.Content.ReadAsStringAsync());
        }

        await signIn.SignOutAsync();
        var clearedCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(".AspNetCore.Identity.Application", clearedCookie);
        Assert.True(clearedCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)
                    || clearedCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
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
