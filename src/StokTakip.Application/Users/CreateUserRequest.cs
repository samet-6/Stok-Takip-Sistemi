using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Users;

// Admin creates a Çalışan (User role). Password-policy details are enforced by
// Identity in the service; here we only guard presence/format at the boundary.
public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(100, ErrorMessage = "En fazla 100 karakter olabilir")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
    [MaxLength(256, ErrorMessage = "En fazla 256 karakter olabilir")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    public string Password { get; set; } = string.Empty;
}
