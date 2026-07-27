using StokTakip.Application.StockMovements;

namespace StokTakip.Application.Products;

public sealed record ProductDetailDto(
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
    // UnitPrice × StockQuantity, multiplied by the database so this row agrees to the
    // kuruş with the totals reported by ProductSummaryDto.
    decimal StockValue,
    bool IsActive,
    uint RowVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Description,
    IReadOnlyList<StockMovementDto> RecentMovements);
