#!/usr/bin/env bash
# blazor-script.test.sh
#
# Regression test for the "Blazor circuit never establishes" bug.
#
# History (3 passes to find the real root cause):
#
#   Pass 1: user reported "terminal Enter does nothing". We refactored the
#   page into TerminalViewModel with ExecuteAsync(string). Did not fix it.
#
#   Pass 2: user reported "blazor.server.js returns 404". We changed
#   _Host.cshtml to reference blazor.web.js instead. User rebuilt. STILL
#   did not fix it - because blazor.web.js loaded (200, not 404) but no
#   SignalR circuit was established. The legacy MapBlazorHub pipeline is
#   not compatible with blazor.web.js.
#
#   Pass 3 (this test guards the definitive fix): full migration to the
#   .NET 8+ Blazor Web App pipeline.
#     - Components/App.razor is the HTML root (replaces _Host.cshtml),
#       references blazor.web.js, and uses <Routes @rendermode="InteractiveServer" />
#     - Components/Routes.razor holds the <Router> (was App.razor)
#     - Program.cs uses AddRazorComponents().AddInteractiveServerComponents()
#       and MapRazorComponents<App>().AddInteractiveServerRenderMode()
#     - The legacy AddServerSideBlazor / MapBlazorHub / MapFallbackToPage
#       are gone
#     - Pages/_Host.cshtml is deleted
#
# This test asserts all of these invariants so we never regress.
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"

assert_contains() {
    local expected="$1" file="$2"
    if [[ ! -f "${file}" ]]; then
        echo "FAIL: file not found: ${file}" >&2
        exit 1
    fi
    if ! grep -Fq -- "${expected}" "${file}"; then
        echo "FAIL: expected content not found in ${file}:" >&2
        echo "  ${expected}" >&2
        echo "--- ${file} ---" >&2
        cat "${file}" >&2
        exit 1
    fi
}

assert_not_contains() {
    local expected="$1" file="$2"
    if [[ ! -f "${file}" ]]; then
        echo "FAIL: file not found: ${file}" >&2
        exit 1
    fi
    if grep -Fq -- "${expected}" "${file}"; then
        echo "FAIL: forbidden content found in ${file}:" >&2
        echo "  ${expected}" >&2
        exit 1
    fi
}

assert_file_missing() {
    local file="$1"
    if [[ -f "${file}" ]]; then
        echo "FAIL: file should be deleted but still exists: ${file}" >&2
        exit 1
    fi
}

# ---------- 1. Components/App.razor (new HTML root) ----------

app_razor="${repo_root}/src/CrowdSecManager/Components/App.razor"
assert_contains '_framework/blazor.web.js' "${app_razor}"
assert_not_contains 'src="_framework/blazor.server.js"' "${app_razor}"
assert_not_contains "src='_framework/blazor.server.js'" "${app_razor}"
assert_contains '<Routes @rendermode="InteractiveServer"' "${app_razor}"
assert_contains '<HeadOutlet @rendermode="InteractiveServer"' "${app_razor}"

# ---------- 2. Components/Routes.razor (the Router, was App.razor) ----------

routes_razor="${repo_root}/src/CrowdSecManager/Components/Routes.razor"
assert_contains '<Router' "${routes_razor}"
assert_contains 'AuthorizeRouteView' "${routes_razor}"

# ---------- 3. Components/_Imports.razor has the using static ----------

imports_razor="${repo_root}/src/CrowdSecManager/Components/_Imports.razor"
assert_contains '@using static Microsoft.AspNetCore.Components.Web.RenderMode' "${imports_razor}"

# ---------- 4. Program.cs uses the new pipeline ----------

program_file="${repo_root}/src/CrowdSecManager/Program.cs"
assert_contains 'AddRazorComponents' "${program_file}"
assert_contains 'AddInteractiveServerComponents' "${program_file}"
assert_contains 'MapRazorComponents' "${program_file}"
assert_contains 'AddInteractiveServerRenderMode' "${program_file}"
assert_contains 'AddCascadingAuthenticationState' "${program_file}"
assert_contains 'UseAntiforgery' "${program_file}"

# ---------- 5. Program.cs does NOT use the legacy pipeline ----------

assert_not_contains 'AddServerSideBlazor' "${program_file}"
assert_not_contains 'MapBlazorHub' "${program_file}"
assert_not_contains 'MapFallbackToPage' "${program_file}"

# ---------- 6. _Host.cshtml is deleted ----------

host_file="${repo_root}/src/CrowdSecManager/Pages/_Host.cshtml"
assert_file_missing "${host_file}"

# ---------- 7. Dockerfile installs ASP.NET 10 runtime (provides blazor.web.js) ----------

dockerfile="${repo_root}/Dockerfile"
assert_contains '--runtime aspnetcore' "${dockerfile}"
assert_contains '--channel 10.0' "${dockerfile}"

# ---------- 8. The new App.razor uses typeof(Routes), not typeof(App), for the Router assembly ----------

assert_contains 'typeof(Routes).Assembly' "${routes_razor}"

echo 'Blazor pipeline migration regression passed (19 assertions).'
