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

    /// <summary>
    /// The lockout is driven through UserManager rather than SignInManager, which lives in the
    /// ASP.NET Core shared framework: using it here would mean giving this class library the
    /// whole web stack, and SignInManager belongs at the web edge anyway. These calls are what
    /// CheckPasswordSignInAsync does internally, minus the cookie and two-factor work — and the
    /// two-factor part would not have helped: its flow is built on an intermediate cookie this
    /// JWT application does not have.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            throw new UnauthorizedException("E-posta veya şifre hatalı");

        if (await _userManager.IsLockedOutAsync(user))
            throw new LockedOutException(LockoutPolicy.Message);

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            // CheckPasswordAsync only compares a hash: it moves no counter and reads no
            // LockoutEnd. Everything that makes the lockout real happens on this line — without
            // it the configured Lockout options were documentation, not a defence.
            await _userManager.AccessFailedAsync(user);

            // The attempt that reaches the limit is itself refused as locked, so the user is told
            // to wait instead of being invited to guess once more.
            if (await _userManager.IsLockedOutAsync(user))
                throw new LockedOutException(LockoutPolicy.Message);

            throw new UnauthorizedException("E-posta veya şifre hatalı");
        }

        // Near-misses spread over a working day must not add up to a lockout nobody can explain.
        // Guarded because the reset is a database write and most logins have nothing to reset.
        //
        // NOTE — if two-factor authentication is added, this moves: with a second factor the
        // sign-in is not complete here, so the counter must only be cleared after the code is
        // verified, and a wrong code must call AccessFailedAsync as well. Otherwise the lockout
        // would guard the password and leave the code unprotected.
        if (await _userManager.GetAccessFailedCountAsync(user) > 0)
            await _userManager.ResetAccessFailedCountAsync(user);

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
