using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Auth;
using StokTakip.Infrastructure.Auth;

namespace StokTakip.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;

    public AuthController(IAuthService authService, ITokenService tokenService)
    {
        _authService = authService;
        _tokenService = tokenService;
    }

    [AllowAnonymous]
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

    /// <summary>
    /// Exchanges the session token for a 30-second, hub-only ticket. The client calls this
    /// on every (re)connection attempt.
    /// </summary>
    /// <remarks>
    /// No DB round trip: reaching this method means JwtBearer already validated the caller's
    /// session for this very request (SecurityStamp + IsActive), so the stamp can simply be
    /// carried over from the incoming token. That validation is also what makes the ticket
    /// fetch double as a session re-check — a revoked session gets 401 here, and the axios
    /// interceptor logs the user out.
    /// </remarks>
    [Authorize]
    [HttpPost("hub-ticket")]
    public ActionResult<HubTicketResponse> HubTicket()
    {
        var userId = User.FindFirstValue("sub");
        var securityStamp = User.FindFirstValue(TokenService.SecurityStampClaimType);
        if (userId is null || securityStamp is null)
            return Unauthorized();

        // Role comes from the caller's own validated session — no database round trip, same
        // as the security stamp above.
        var ticket = _tokenService.CreateHubToken(userId, securityStamp, User.FindFirstValue("role"));
        return Ok(new HubTicketResponse(ticket.Token, ticket.ExpiresAt));
    }
}
