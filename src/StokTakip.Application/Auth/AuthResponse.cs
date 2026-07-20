namespace StokTakip.Application.Auth;

public sealed record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    UserDto User);
