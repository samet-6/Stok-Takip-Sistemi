using System.ComponentModel.DataAnnotations;

namespace StokTakip.Application.Products;

public sealed class CreateProductRequest
{
    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(150, ErrorMessage = "En fazla 150 karakter olabilir")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bu alan zorunludur")]
    [MaxLength(30, ErrorMessage = "En fazla 30 karakter olabilir")]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "En fazla 500 karakter olabilir")]
    public string? Description { get; set; }

    public int CategoryId { get; set; }

    public int SupplierId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Fiyat negatif olamaz")]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Minimum stok negatif olamaz")]
    public int MinStockLevel { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Başlangıç stoğu en az 1 olmalıdır")]
    public int? InitialStock { get; set; }
}
