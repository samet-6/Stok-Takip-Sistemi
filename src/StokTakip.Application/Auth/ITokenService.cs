namespace StokTakip.Application.Auth;

public sealed record TokenResult(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    TokenResult CreateToken(string userId, string email, string fullName, IList<string> roles);
}
