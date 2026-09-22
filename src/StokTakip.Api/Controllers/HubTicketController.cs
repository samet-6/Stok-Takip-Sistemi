using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Auth;
using StokTakip.Infrastructure.Auth;

namespace StokTakip.Api.Controllers;

/// <summary>
/// The realtime layer's own credential, kept apart from <see cref="AuthController"/>: that one
/// answers "who are you" against the database, this one mints a short-lived transport token from
/// a session that was already proven. Different dependency, different lifetime, no shared code.
/// <para>
/// The route stays under <c>api/auth</c> because the URL is part of the published contract —
/// only the file split is new.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class HubTicketController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public HubTicketController(ITokenService tokenService) => _tokenService = tokenService;

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
