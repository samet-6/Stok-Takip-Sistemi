using StokTakip.Application.Common;

namespace StokTakip.Application.StockMovements;

public interface IStockMovementService
{
    Task<PagedResult<StockMovementDto>> GetPagedAsync(
        StockMovementQuery query,
        CancellationToken ct);

    Task<StockMovementResponse> CreateAsync(
        CreateStockMovementRequest request,
        string userId,
        CancellationToken ct);
}
