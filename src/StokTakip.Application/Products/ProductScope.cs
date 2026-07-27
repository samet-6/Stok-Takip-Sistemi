namespace StokTakip.Application.Products;

// The catalog dimensions a request is about. Deliberately separate from ProductQuery,
// which adds list-only concerns (search, paging, IncludeInactive, LowStockOnly) — an
// endpoint that binds ProductScope cannot accept a parameter it would silently ignore.
public sealed record ProductScope(
    int? CategoryId = null,
    int? SupplierId = null);
