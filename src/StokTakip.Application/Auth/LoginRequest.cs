using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Auth;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    public string Password { get; set; } = string.Empty;
}
