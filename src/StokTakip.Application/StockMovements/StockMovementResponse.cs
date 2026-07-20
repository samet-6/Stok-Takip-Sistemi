namespace StokTakip.Application.StockMovements;

public sealed record StockMovementResponse(
    StockMovementDto Movement,
    int NewStockQuantity);
