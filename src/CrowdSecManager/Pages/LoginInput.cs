using System.ComponentModel.DataAnnotations;

namespace CrowdSecManager.Pages;

/// <summary>Form payload bound by the login Razor Page.</summary>
public sealed class LoginInput
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}
