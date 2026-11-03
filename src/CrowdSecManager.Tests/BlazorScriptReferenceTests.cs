using Xunit;

namespace CrowdSecManager.Tests;

/// <summary>
/// Regression tests for the "Blazor circuit never establishes" bug.
///
/// The original symptom was: "the terminal doesn't work, I can type but
/// Enter does nothing" + "blazor.server.js returns 404".
///
/// Root cause (definitively diagnosed on the 3rd pass): the project had
/// been migrated from .NET 7 to .NET 10, but the Blazor pipeline was
/// left in the legacy configuration:
///   - <c>_Host.cshtml</c> referenced <c>_framework/blazor.server.js</c>
///   - <c>Program.cs</c> used <c>AddServerSideBlazor</c> +
///     <c>MapBlazorHub</c> + <c>MapFallbackToPage("/_Host")</c>
///
/// That legacy pipeline is designed for <c>blazor.server.js</c>. A first
/// fix attempt swapped the script reference to <c>blazor.web.js</c> but
/// KEPT the legacy pipeline - and <c>blazor.web.js</c> does not
/// establish a SignalR circuit with <c>MapBlazorHub</c> (it expects
/// <c>MapRazorComponents</c>). The browser loaded the script (200, not
/// 404) but no circuit was opened, so every <c>@@onclick</c> /
/// <c>@@onkeydown</c> / <c>@@bind</c> handler stayed dead.
///
/// The definitive fix is the full migration to the .NET 8+ Blazor Web
/// App pipeline:
///   - <c>AddRazorComponents().AddInteractiveServerComponents()</c> in
///     <c>Program.cs</c>
///   - <c>MapRazorComponents&lt;App&gt;().AddInteractiveServerRenderMode()</c>
///   - <c>Components/App.razor</c> is now the HTML root (was
///     <c>_Host.cshtml</c>) and references <c>blazor.web.js</c>
///   - <c>Components/Routes.razor</c> holds the <c>&lt;Router&gt;</c>
///     (was <c>App.razor</c>)
///   - <c>Pages/_Host.cshtml</c> is deleted
///
/// These tests assert that this configuration stays in place.
/// </summary>
public sealed class BlazorScriptReferenceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(Directory.Exists(Path.Combine(dir.FullName, "src"))
                    && Directory.Exists(Path.Combine(dir.FullName, "deploy"))))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "could not locate repo root from test assembly path");
        return dir!.FullName;
    }

    private static string ReadAppRazor()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "CrowdSecManager", "Components", "App.razor");
        Assert.True(File.Exists(path), $"Components/App.razor not found at {path}");
        return File.ReadAllText(path);
    }

    private static string ReadRoutesRazor()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "CrowdSecManager", "Components", "Routes.razor");
        Assert.True(File.Exists(path), $"Components/Routes.razor not found at {path}");
        return File.ReadAllText(path);
    }

    private static string ReadProgramCs()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "CrowdSecManager", "Program.cs");
        Assert.True(File.Exists(path), $"Program.cs not found at {path}");
        return File.ReadAllText(path);
    }

    private static string ReadImportsRazor()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "CrowdSecManager", "Components", "_Imports.razor");
        Assert.True(File.Exists(path), $"Components/_Imports.razor not found at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void AppRazor_ReferencesUnifiedBlazorWebJsScript()
    {
        var app = ReadAppRazor();
        Assert.Contains("_framework/blazor.web.js", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRazor_DoesNotReferenceLegacyBlazorServerJsScript()
    {
        var app = ReadAppRazor();
        // We match the script-tag src pattern (not the bare name) so the
        // explanatory comment in App.razor that mentions the legacy name
        // in prose does not trigger a false positive.
        Assert.DoesNotContain("src=\"_framework/blazor.server.js\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("src='_framework/blazor.server.js'", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRazor_UsesInteractiveServerRenderModeOnRoutes()
    {
        var app = ReadAppRazor();
        Assert.Contains("<Routes @rendermode=\"InteractiveServer\" />", app, StringComparison.Ordinal);
        Assert.Contains("<HeadOutlet @rendermode=\"InteractiveServer\" />", app, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutesRazor_ContainsRouter()
    {
        var routes = ReadRoutesRazor();
        Assert.Contains("<Router", routes, StringComparison.Ordinal);
        Assert.Contains("AuthorizeRouteView", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramCs_UsesNewRazorComponentsPipeline()
    {
        var program = ReadProgramCs();
        // The new .NET 8+ pipeline.
        Assert.Contains("AddRazorComponents", program, StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.Contains("MapRazorComponents", program, StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerRenderMode", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramCs_DoesNotUseLegacyServerSideBlazorPipeline()
    {
        var program = ReadProgramCs();
        // The legacy pipeline is incompatible with blazor.web.js. If anyone
        // reverts to AddServerSideBlazor / MapBlazorHub / MapFallbackToPage,
        // the circuit breaks again.
        Assert.DoesNotContain("AddServerSideBlazor", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapFallbackToPage", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportsRazor_HasUsingStaticRenderMode()
    {
        // The @rendermode="InteractiveServer" directive in App.razor needs
        // the using static to bring InteractiveServer into scope.
        var imports = ReadImportsRazor();
        Assert.Contains("@using static Microsoft.AspNetCore.Components.Web.RenderMode",
            imports, StringComparison.Ordinal);
    }

    [Fact]
    public void HostCshtml_IsDeleted()
    {
        // The legacy _Host.cshtml has been replaced by Components/App.razor.
        // If someone re-creates it, the fallback would be ambiguous.
        var path = Path.Combine(FindRepoRoot(), "src", "CrowdSecManager", "Pages", "_Host.cshtml");
        Assert.False(File.Exists(path),
            $"_Host.cshtml should be deleted (replaced by Components/App.razor), but was found at {path}");
    }
}
