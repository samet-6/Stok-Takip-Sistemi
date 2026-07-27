using Microsoft.AspNetCore.Identity;
using StokTakip.Application.Auth;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;

    public AuthService(UserManager<ApplicationUser> userManager, ITokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedException("E-posta veya şifre hatalı");

        // Soft-deleted (deactivated) users cannot log in; audit trail is preserved.
        if (!user.IsActive)
            throw new UnauthorizedException("Hesabınız pasif durumda. Yönetici ile iletişime geçin.");

        var roles = await _userManager.GetRolesAsync(user);
        return BuildResponse(user, roles);
    }

    public async Task<UserDto?> GetMeAsync(string userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.Email!, user.FullName, roles.ToList());
    }

    public async Task<ChangePasswordResponse> ChangePasswordAsync(
        string userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UnauthorizedException("Kimlik doğrulama gerekli");

        // Reusing the same password is a no-op that would still bump the SecurityStamp —
        // reject it explicitly (Identity's ChangePasswordAsync does not).
        if (request.NewPassword == request.CurrentPassword)
            throw new BadRequestException(
                new Dictionary<string, string[]> { ["newPassword"] = ["Yeni şifre mevcut şifreden farklı olmalı."] });

        // ChangePasswordAsync verifies the current password and bumps the SecurityStamp
        // on success — which would invalidate the caller's own token. We re-issue a fresh
        // JWT below (carrying the new stamp) so the user stays logged in.
        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            // Wrong current password vs. new-password policy violation — surface under the
            // matching field so the frontend can highlight the right input.
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                throw new BadRequestException(
                    new Dictionary<string, string[]> { ["currentPassword"] = ["Mevcut şifre hatalı."] });

            throw new BadRequestException(
                new Dictionary<string, string[]> { ["newPassword"] = [PasswordPolicy.Message] });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.CreateToken(
            user.Id, user.Email!, user.FullName, user.SecurityStamp!, roles);
        return new ChangePasswordResponse(token.Token, token.ExpiresAt);
    }

    private AuthResponse BuildResponse(ApplicationUser user, IList<string> roles)
    {
        var token = _tokenService.CreateToken(
            user.Id, user.Email!, user.FullName, user.SecurityStamp!, roles);
        var userDto = new UserDto(user.Id, user.Email!, user.FullName, roles.ToList());
        return new AuthResponse(token.Token, token.ExpiresAt, userDto);
    }
}
