using CrowdSecManager.Areas.Identity.Data;
using CrowdSecManager.Data;
using CrowdSecManager.Models;
using CrowdSecManager.Services;
using System.Collections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var processEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    processEnvironment[entry.Key.ToString()!] = entry.Value?.ToString();
builder.Configuration.AddInMemoryCollection(CrowdSecEnvironmentMapper.ToConfiguration(processEnvironment));

// ---- Configuration bindings ----
builder.Services.Configure<CrowdSecOptions>(builder.Configuration.GetSection("CrowdSec"));
builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection("GeoIp"));

// ---- Database (SQLite, app metadata only) ----
var dbPath = builder.Configuration["Database:Path"]
             ?? "/var/lib/crowdsec-manager/app.db";
var keyDirectory = builder.Configuration["DataProtection:KeysPath"]
                   ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "keys");
Directory.CreateDirectory(keyDirectory);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(keyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// Make sure the parent dir exists (esp. in dev)
try
{
    var dir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);
}
catch { /* ignored - container will mount the volume */ }

// ---- ASP.NET Identity ----
builder.Services
    .AddDefaultIdentity<ApplicationUser>(o =>
    {
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.Password.RequiredLength = 10;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    o.LogoutPath = "/logout";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ---- Blazor Server ----
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(o =>
{
    o.DetailedErrors = builder.Environment.IsDevelopment();
    o.DisconnectedCircuitMaxRetained = 30;
    o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
});

// ---- SignalR (used by the live threat map) ----
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 256 * 1024;
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

// ---- CrowdSec integration services ----
builder.Services.AddHttpClient<ICrowdSecApiService, CrowdSecApiService>();
builder.Services.AddSingleton<ICrowdSecProcessRunner, CrowdSecProcessRunner>();
builder.Services.AddSingleton<ICrowdSecCliService, CrowdSecCliService>();
builder.Services.AddSingleton<IGeoIpService, GeoIpService>();
builder.Services.AddSingleton<IThreatMapEventBus, ThreatMapEventBus>();
builder.Services.AddScoped<IConfigFileService, ConfigFileService>();
builder.Services.AddHostedService<ThreatFeedService>();

var app = builder.Build();

// ---- Pipeline ----
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }));
app.MapFallbackToPage("/_Host");
app.MapRazorPages();

// ---- DB bootstrap + seed admin ----
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    try
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        await DbSeeder.SeedAsync(sp);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "DB bootstrap or administrator seeding failed; refusing to start without a usable identity store.");
        throw;
    }
}

app.Run();

public partial class Program { }
