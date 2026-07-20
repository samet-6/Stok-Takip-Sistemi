using StokTakip.Domain.Common;

namespace StokTakip.Domain.Entities;

public class Supplier : IAuditable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
