namespace StokTakip.Application.Products;

public sealed record ProductQuery(
    string? Search = null,
    int? CategoryId = null,
    int? SupplierId = null,
    bool LowStockOnly = false,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 10);
