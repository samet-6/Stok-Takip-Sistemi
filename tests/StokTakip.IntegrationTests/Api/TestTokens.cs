using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StokTakip.Infrastructure.Auth;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Builds tokens by hand — the one place in the suite where that is the point rather than a
/// shortcut. Everywhere else tests log in for real (see <see cref="AuthenticatedClient"/>);
/// here the subject under test is token validation itself, which can only be exercised with
/// tokens the application would never issue.
/// </summary>
internal static class TestTokens
{
    /// <summary>
    /// Signs a token with the given claims. Defaults produce a token the application accepts,
    /// so each test changes exactly one thing and the rejection has a single explanation.
    /// </summary>
    public static string Create(
        IEnumerable<Claim> claims,
        DateTime? expires = null,
        string key = StokTakipFactory.JwtKey,
        string issuer = StokTakipFactory.JwtIssuer,
        string audience = StokTakipFactory.JwtAudience)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            // Always safely before expiry, so "not yet valid" can never be the reason a test
            // sees a 401 when it is testing something else.
            notBefore: expiry.AddHours(-8),
            expires: expiry,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// The claims a session token carries. Read from the database because the security stamp is
    /// never exposed over HTTP — and a token without the current stamp is rejected per request.
    /// </summary>
    public static async Task<Claim[]> SessionClaimsAsync(
        TestDatabaseFixture db, string email, string? role, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        var user = await context.Users
            .Where(u => u.Email == email)
            .Select(u => new { u.Id, u.SecurityStamp })
            .SingleAsync(ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(TokenService.SecurityStampClaimType, user.SecurityStamp!)
        };

        if (role is not null)
            claims.Add(new Claim("role", role));

        return [.. claims];
    }
}
