using System.ComponentModel.DataAnnotations;

namespace CrowdSecManager.ViewModels;

public sealed class AccountPasswordChangeForm
{
    [Required(ErrorMessage = "Enter your current password.")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "The new password must be at least 10 characters.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The password confirmation does not match.")]
    public string ConfirmPassword { get; set; } = "";
}
