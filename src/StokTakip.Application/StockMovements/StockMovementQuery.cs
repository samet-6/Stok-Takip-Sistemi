using StokTakip.Domain.Enums;

namespace StokTakip.Application.StockMovements;

public sealed record StockMovementQuery(
    int? ProductId = null,
    string? UserId = null,
    int? SupplierId = null,
    int? CategoryId = null,
    StockMovementType? Type = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 10);
