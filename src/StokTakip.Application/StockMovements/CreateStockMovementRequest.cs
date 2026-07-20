using System.ComponentModel.DataAnnotations;
using StokTakip.Domain.Enums;

namespace StokTakip.Application.StockMovements;

public sealed class CreateStockMovementRequest
{
    public int ProductId { get; set; }

    public StockMovementType Type { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Miktar en az 1 olmalıdır")]
    public int Quantity { get; set; }

    [MaxLength(300, ErrorMessage = "En fazla 300 karakter olabilir")]
    public string? Note { get; set; }
}
