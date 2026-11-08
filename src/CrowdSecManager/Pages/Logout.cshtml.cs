using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CrowdSecManager.Pages;

[Authorize]
public sealed class LogoutModel(SignInManager<Areas.Identity.Data.ApplicationUser> signIn) : PageModel
{
    private readonly SignInManager<Areas.Identity.Data.ApplicationUser> _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return Redirect("/login");
    }
}
