using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StokTakip.Application.Auth;

namespace StokTakip.Api.Controllers;

/// <summary>
/// Session and identity: proving who the caller is, and reporting it back. The hub's
/// short-lived transport ticket is a different concern and lives in
/// <see cref="HubTicketController"/>, on the same route prefix.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _authService.LoginAsync(request, ct));

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null)
            return Unauthorized();

        var user = await _authService.GetMeAsync(userId, ct);
        return user is null ? Unauthorized() : Ok(user);
    }
}
