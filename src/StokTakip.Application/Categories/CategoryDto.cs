namespace StokTakip.Application.Categories;

public sealed record CategoryDto(
    int Id,
    string Name,
    string? Description,
    int ProductCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
