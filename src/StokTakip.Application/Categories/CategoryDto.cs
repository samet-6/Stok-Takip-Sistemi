namespace StokTakip.Application.Categories;

public sealed record CategoryDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    // Null while active, and also null for rows switched off before the stamp existed:
    // the UI reads it as "unknown", never as "just now".
    DateTime? DeactivatedAt,
    int ProductCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
