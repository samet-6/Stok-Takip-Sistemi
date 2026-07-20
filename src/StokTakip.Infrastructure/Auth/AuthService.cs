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

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (await _userManager.FindByEmailAsync(request.Email) is not null)
            throw new ConflictException("Bu e-posta zaten kayıtlı");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new BadRequestException(result.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, "User");

        var roles = await _userManager.GetRolesAsync(user);
        return BuildResponse(user, roles);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedException("E-posta veya şifre hatalı");

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

    private AuthResponse BuildResponse(ApplicationUser user, IList<string> roles)
    {
        var token = _tokenService.CreateToken(user.Id, user.Email!, user.FullName, roles);
        var userDto = new UserDto(user.Id, user.Email!, user.FullName, roles.ToList());
        return new AuthResponse(token.Token, token.ExpiresAt, userDto);
    }
}
