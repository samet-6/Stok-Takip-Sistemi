namespace StokTakip.Application.Products;

public sealed record ProductQuery(
    string? Search = null,
    int? CategoryId = null,
    int? SupplierId = null,
    bool LowStockOnly = false,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 10)
{
    // The catalog dimensions of this query, so the list and the summary narrow products
    // through one shared code path instead of two predicates that can drift apart.
    public ProductScope Scope => new(CategoryId, SupplierId);
}
