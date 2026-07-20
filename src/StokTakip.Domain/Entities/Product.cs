using StokTakip.Domain.Common;

namespace StokTakip.Domain.Entities;

public class Product : IAuditable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string SKU { get; set; }
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public int SupplierId { get; set; }
    public decimal UnitPrice { get; set; }
    public int StockQuantity { get; set; }
    public int MinStockLevel { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
}
