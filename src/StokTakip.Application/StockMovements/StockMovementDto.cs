using StokTakip.Domain.Enums;

namespace StokTakip.Application.StockMovements;

public sealed record StockMovementDto(
    int Id,
    int ProductId,
    string ProductName,
    // The ledger keeps showing a product after it leaves the catalogue — hiding the row would
    // change past totals. The flag lets the UI mark it "(pasif)" instead of staying silent.
    bool ProductIsActive,
    StockMovementType Type,
    int Quantity,
    string? Note,
    DateTime CreatedAt,
    string CreatedByUserId,
    string CreatedByFullName);
