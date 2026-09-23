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

    /// <summary>
    /// The client's key for this movement's intent (D27): the same intent sent twice is one
    /// movement. Null only on rows written before the key was required.
    /// </summary>
    public Guid? IdempotencyKey { get; set; }

    public Product Product { get; set; } = null!;
}
