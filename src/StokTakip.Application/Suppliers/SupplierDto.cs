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
    DateTime CreatedAt,
    DateTime UpdatedAt);
