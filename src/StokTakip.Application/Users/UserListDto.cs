namespace StokTakip.Application.Users;

// Read DTO for the Çalışanlar admin page. createdAt shown as "İşe Giriş",
// deactivatedAt (nullable) as "İşten Çıkış" (populated only for passive users).
public sealed record UserListDto(
    string Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? DeactivatedAt);
