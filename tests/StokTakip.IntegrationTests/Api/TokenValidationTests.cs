using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using StokTakip.Infrastructure.Auth;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Everything a client could send instead of a valid session. Each test changes exactly one
/// property of an otherwise acceptable token, so a 401 has a single possible explanation.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TokenValidationTests
{
    private const string ProtectedEndpoint = "/api/products";
    private const string AdminEndpoint = "/api/users";

    private readonly TestDatabaseFixture _db;

    public TokenValidationTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tokensiz_korumali_istek_401_ve_ProblemDetails_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await client.GetAsync(ProtectedEndpoint, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(401, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
    }

    /// <summary>
    /// The 403 half of the same handler. Both JWT paths write their body by hand, so neither is
    /// covered by the ProblemDetails middleware the rest of the API goes through.
    /// </summary>
    [Fact]
    public async Task Yetkisiz_rolun_403_yaniti_da_ProblemDetails_donuyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var response = await calisan.PostAsJsonAsync(AdminEndpoint, new { }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(403, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Bozuk_imzali_token_401_aliyor()
    {
        var client = _db.Factory.CreateClient();
        var token = await AuthenticatedClient.LoginAsync(
            client, StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword, Ct);

        using var tampered = _db.Factory.WithToken(Tamper(token));
        var response = await tampered.GetAsync(ProtectedEndpoint, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Suresi_dolmus_token_401_aliyor()
    {
        var claims = await TestTokens.SessionClaimsAsync(_db, StokTakipFactory.AdminEmail, "Admin", Ct);

        // Well past the 30-second clock skew the application allows.
        using var expired = _db.Factory.WithToken(TestTokens.Create(claims, DateTime.UtcNow.AddHours(-1)));
        var response = await expired.GetAsync(ProtectedEndpoint, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Guard: the very same claims are accepted when not expired, so expiry is the only
        // difference between the two requests.
        using var fresh = _db.Factory.WithToken(TestTokens.Create(claims));
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync(ProtectedEndpoint, Ct)).StatusCode);
    }

    /// <summary>
    /// The one that matters most: without signature verification, anyone could mint a token
    /// claiming the Admin role and the application would believe it.
    /// </summary>
    [Fact]
    public async Task Baska_anahtarla_imzalanmis_Admin_tokeni_401_aliyor()
    {
        var claims = await TestTokens.SessionClaimsAsync(_db, StokTakipFactory.AdminEmail, "Admin", Ct);

        using var forged = _db.Factory.WithToken(TestTokens.Create(
            claims, key: "saldirganin-uydurdugu-anahtar-0123456789-abcdefghij"));
        var response = await forged.GetAsync(AdminEndpoint, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Guard: signed with the application's own key the identical token opens the admin-only
        // endpoint — so it was the signature that was refused, not the claims.
        using var genuine = _db.Factory.WithToken(TestTokens.Create(claims));
        Assert.Equal(HttpStatusCode.OK, (await genuine.GetAsync(AdminEndpoint, Ct)).StatusCode);
    }

    [Fact]
    public async Task Sub_claimi_olmayan_token_401_aliyor()
    {
        var full = await TestTokens.SessionClaimsAsync(_db, StokTakipFactory.AdminEmail, "Admin", Ct);
        var withoutSub = full.Where(c => c.Type != JwtRegisteredClaimNames.Sub).ToArray();

        // Sanity: the stamp is still there, so "sub" really is the only thing missing.
        Assert.Contains(withoutSub, c => c.Type == TokenService.SecurityStampClaimType);

        using var client = _db.Factory.WithToken(TestTokens.Create(withoutSub));
        var response = await client.GetAsync(ProtectedEndpoint, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Flips one character of the signature segment, leaving header and payload intact.</summary>
    private static string Tamper(string token)
    {
        var lastCharacter = token[^1];

        return token[..^1] + (lastCharacter == 'A' ? 'B' : 'A');
    }
}
