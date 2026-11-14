using CrowdSecManager.Areas.Identity.Data;
using CrowdSecManager.Data;
using CrowdSecManager.Infrastructure;
using CrowdSecManager.Services;
using System.Collections;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

var processEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    processEnvironment[entry.Key.ToString()!] = entry.Value?.ToString();
builder.Configuration.AddInMemoryCollection(CrowdSecEnvironmentMapper.ToConfiguration(processEnvironment));

builder.Services.AddInfrastructure(builder.Configuration);

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
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o =>
    {
        o.DetailedErrors = builder.Environment.IsDevelopment();
    });
builder.Services.AddTransient<CrowdSecManager.ViewModels.TerminalViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.DashboardViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.AlertsViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.BouncersViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.DecisionsViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.MachinesViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.SimulationViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.HubManagementViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.ExplainViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.LapiViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.ConfigEditorViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.ThreatMapViewModel>();
builder.Services.AddTransient<CrowdSecManager.ViewModels.NotificationsViewModel>();
builder.Services.AddScoped<IAppBackupService, AppBackupService>();
builder.Services.AddScoped<IWindowsClientProvisioningService, WindowsClientProvisioningService>();

// ---- SignalR (used by the live threat map) ----
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 256 * 1024;
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }));
app.MapGet("/api/backup", async (HttpContext context, IAppBackupService backup, CancellationToken ct) =>
{
    context.Response.ContentType = "application/zip";
    context.Response.Headers.ContentDisposition = $"attachment; filename=\"crowdsec-manager-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip\"";
    await backup.WriteBackupAsync(context.Response.Body, ct);
}).RequireAuthorization();
app.MapRazorComponents<CrowdSecManager.Components.App>()
    .AddInteractiveServerRenderMode();
app.MapRazorPages();

await app.Services.InitializeDatabaseAsync();

app.Run();
