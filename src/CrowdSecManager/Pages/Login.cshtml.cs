using CrowdSecManager.Areas.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CrowdSecManager.Pages;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    private readonly SignInManager<ApplicationUser> _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        if (!Url.IsLocalUrl(ReturnUrl)) ReturnUrl = "/";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Url.IsLocalUrl(ReturnUrl)) ReturnUrl = "/";
        if (!ModelState.IsValid) return Page();

        var result = await _signIn.PasswordSignInAsync(
            Input.Username, Input.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(ReturnUrl!);

        ErrorMessage = result.IsLockedOut
            ? "Account is locked out. Try again later."
            : result.RequiresTwoFactor
                ? "Two-factor authentication is required (not configured)."
                : "Invalid username or password.";
        return Page();
    }
}
