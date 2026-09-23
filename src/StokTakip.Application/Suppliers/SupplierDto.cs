namespace StokTakip.Application.Suppliers;

public sealed record SupplierDto(
    int Id,
    string Name,
    string ContactEmail,
    string? Phone,
    string? Address,
    bool IsActive,
    DateTime? DeactivatedAt,
    int ProductCount,
    uint RowVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);
