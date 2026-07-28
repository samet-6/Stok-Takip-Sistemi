namespace StokTakip.Application.Auth;

public sealed record TokenResult(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    TokenResult CreateToken(
        string userId, string email, string fullName, string securityStamp, IList<string> roles);

    /// <summary>
    /// Mints a short-lived, hub-only ticket. The browser cannot put an Authorization
    /// header on a WebSocket handshake, so the identity has to travel in the query
    /// string — and query strings land in access logs. This is what goes there instead
    /// of the 8-hour session token.
    /// </summary>
    /// <param name="role">
    /// Carried so the hub can place the connection in its broadcast groups without a database
    /// round trip. Null for a user with no role.
    /// </param>
    TokenResult CreateHubToken(string userId, string securityStamp, string? role);
}
