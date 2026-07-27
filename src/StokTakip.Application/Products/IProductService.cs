using StokTakip.Application.Common;

namespace StokTakip.Application.Products;

public interface IProductService
{
    Task<PagedResult<ProductListDto>> GetPagedAsync(ProductQuery query, CancellationToken ct);

    /// <summary>
    /// Inventory totals over the same scope the list endpoint narrows by. Counting and
    /// summing happen in the database, so the result is exact at any row count.
    /// </summary>
    Task<ProductSummaryDto> GetSummaryAsync(ProductScope scope, CancellationToken ct);
    Task<ProductDetailDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<ProductDetailDto> CreateAsync(CreateProductRequest request, string userId, CancellationToken ct);
    Task<ProductDetailDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes a product. Returns null when hard-deleted (no movements),
    /// or the soft-deleted product (IsActive = false) when it has movements.
    /// </summary>
    Task<ProductListDto?> DeleteAsync(int id, CancellationToken ct);
}
