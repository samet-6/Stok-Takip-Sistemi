using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Categories;

public sealed class UpdateCategoryRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(100, ErrorMessage = "En fazla 100 karakter olabilir")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "En fazla 500 karakter olabilir")]
    public string? Description { get; set; }
}
