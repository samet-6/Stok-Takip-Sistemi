using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Auth;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(100, ErrorMessage = "En fazla 100 karakter olabilir")]
    public string FullName { get; set; } = string.Empty;
}
