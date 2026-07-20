using StokTakip.Domain.Common;
using StokTakip.Domain.Enums;

namespace StokTakip.Domain.Entities;

public class StockMovement : IHasCreatedAt
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public StockMovementType Type { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product Product { get; set; } = null!;
}
