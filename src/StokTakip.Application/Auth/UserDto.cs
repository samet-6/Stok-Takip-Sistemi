namespace StokTakip.Application.Auth;

public sealed record UserDto(
    string Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
