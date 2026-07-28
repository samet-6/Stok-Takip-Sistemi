using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StokTakip.Application.Auth;

namespace StokTakip.Infrastructure.Auth;

public sealed class TokenService : ITokenService
{
    // Claim carrying the user's Identity SecurityStamp; validated per-request in
    // JwtBearer OnTokenValidated (Program.cs). Same string used on the read side.
    public const string SecurityStampClaimType = "sstamp";

    // Marks a token as a hub ticket. Program.cs fences both directions with it:
    // a ticket is refused outside /hubs, and /hubs refuses anything that isn't one.
    public const string ScopeClaimType = "scope";
    public const string HubScope = "hub";

    // Long enough for the whole handshake — the SignalR JS client reuses one value for
    // the negotiate POST and the WebSocket connect — short enough that a ticket leaked
    // through a proxy log is already dead. Note the 30s JwtBearer ClockSkew widens the
    // effective acceptance window to ~60s; still far below a session's lifetime.
    private static readonly TimeSpan HubTicketLifetime = TimeSpan.FromSeconds(30);

    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public TokenResult CreateToken(
        string userId, string email, string fullName, string securityStamp, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SecurityStampClaimType, securityStamp)
        };

        foreach (var role in roles)
            claims.Add(new Claim("role", role));

        return Write(claims, TimeSpan.FromHours(_options.ExpiryHours));
    }

    public TokenResult CreateHubToken(string userId, string securityStamp, string? role)
    {
        // Minimum claims: enough to identify the connection, re-check the session, and decide
        // which broadcast groups the connection belongs to. No email, no profile — a leaked
        // ticket is still not a session summary.
        //
        // The role earns its place: notification signals go to an admins-only
        // group, and the hub has to know the audience at connect time. The alternative was a
        // database round trip per connection to ask a question the caller's own session had
        // already answered. It leaks nothing worth having — anyone holding this ticket can
        // already open a connection as that user, and the role is not a secret from them
        // (the UI branches on it client-side).
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SecurityStampClaimType, securityStamp),
            new(ScopeClaimType, HubScope)
        };

        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim("role", role));

        return Write(claims, HubTicketLifetime);
    }

    private TokenResult Write(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // token.ValidTo is the exp value (UTC, truncated to whole seconds) — keep ExpiresAt identical.
        return new TokenResult(tokenString, token.ValidTo);
    }
}
