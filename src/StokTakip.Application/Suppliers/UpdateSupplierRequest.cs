using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Suppliers;

public sealed class UpdateSupplierRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(150, ErrorMessage = "En fazla 150 karakter olabilir")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
    [MaxLength(150, ErrorMessage = "En fazla 150 karakter olabilir")]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "En fazla 20 karakter olabilir")]
    public string? Phone { get; set; }

    [MaxLength(300, ErrorMessage = "En fazla 300 karakter olabilir")]
    public string? Address { get; set; }

    public bool IsActive { get; set; }
}
