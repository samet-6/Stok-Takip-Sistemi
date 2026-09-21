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

    /// <summary>
    /// Answers with the list shape, not the detail one: the client adds the new row to the list
    /// it is already on, and calls GetByIdAsync when it wants the detail. A detail answer here
    /// would mean the last ten movements plus a user-name lookup, built for a product that has
    /// at most one movement and then discarded (see docs/api_sozlesmesi.md).
    /// </summary>
    Task<ProductListDto> CreateAsync(CreateProductRequest request, string userId, CancellationToken ct);
    /// <summary>
    /// Returns nothing: the endpoint answers 204 and the client refetches the fresh rowVersion
    /// (see docs/api_sozlesmesi.md). Building the detail DTO here also cost a movements query
    /// and a user-name lookup per update, all of it discarded by the controller.
    /// </summary>
    Task UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes a product. Returns null when hard-deleted (no movements),
    /// or the soft-deleted product (IsActive = false) when it has movements.
    /// </summary>
    Task<ProductListDto?> DeleteAsync(int id, CancellationToken ct);
}
