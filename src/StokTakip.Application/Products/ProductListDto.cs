namespace StokTakip.Application.Products;

public sealed record ProductListDto(
    int Id,
    string Name,
    string SKU,
    int CategoryId,
    string CategoryName,
    int SupplierId,
    string SupplierName,
    decimal UnitPrice,
    int StockQuantity,
    int MinStockLevel,
    bool IsActive,
    uint RowVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);
