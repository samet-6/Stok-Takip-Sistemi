namespace StokTakip.Application.Products;

// Inventory totals for a ProductScope, aggregated by the database.
// Active-vs-passive and low-stock are the breakdown this DTO reports, never inputs.
// TotalStockValue is current UnitPrice × current StockQuantity over ACTIVE products —
// a present-day valuation, not a cost basis: repricing a product changes it retroactively.
public sealed record ProductSummaryDto(
    int TotalProducts,
    int ActiveCount,
    int PassiveCount,
    int LowStockCount,
    decimal TotalStockValue);
