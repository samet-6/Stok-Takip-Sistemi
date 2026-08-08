using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Users;

// Edit an active Çalışan. Password is OPTIONAL: null/empty = unchanged;
// filled = admin reset (subject to the same policy as create).
public sealed class UpdateUserRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(100, ErrorMessage = "En fazla 100 karakter olabilir")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
    [MaxLength(256, ErrorMessage = "En fazla 256 karakter olabilir")]
    public string Email { get; set; } = string.Empty;

    public string? Password { get; set; }
}
