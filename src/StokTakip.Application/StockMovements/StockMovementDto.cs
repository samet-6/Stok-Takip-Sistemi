using StokTakip.Domain.Enums;

namespace StokTakip.Application.StockMovements;

public sealed record StockMovementDto(
    int Id,
    int ProductId,
    string ProductName,
    StockMovementType Type,
    int Quantity,
    string? Note,
    DateTime CreatedAt,
    string CreatedByUserId,
    string CreatedByFullName);
