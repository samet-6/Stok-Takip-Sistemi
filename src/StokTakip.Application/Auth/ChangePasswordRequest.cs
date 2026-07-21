using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Auth;

// Self change-password (bank-style): current password required, new password
// subject to the same policy as admin-set passwords.
public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    public string NewPassword { get; set; } = string.Empty;
}
