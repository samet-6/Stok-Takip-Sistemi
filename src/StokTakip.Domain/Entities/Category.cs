using StokTakip.Domain.Common;

namespace StokTakip.Domain.Entities;

public class Category : IAuditable, IDeactivatable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
