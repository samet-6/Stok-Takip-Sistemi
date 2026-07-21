namespace StokTakip.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto?> GetMeAsync(string userId, CancellationToken ct);
    Task<ChangePasswordResponse> ChangePasswordAsync(
        string userId, ChangePasswordRequest request, CancellationToken ct);
}
