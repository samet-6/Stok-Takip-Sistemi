using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Realtime;

/// <summary>
/// The hub's front door, tested over plain HTTP — no SignalR client involved. Everything here
/// happens before a connection exists: what the ticket carries, what the negotiate endpoint
/// accepts, and the scope fence that keeps the two token kinds in their own lanes.
///
/// That fence is the reason the ticket exists at all. A browser cannot put an Authorization header
/// on a WebSocket handshake, so the identity has to ride in the query string — and query strings
/// land in access logs. A 30-second ticket there is a different proposition from an 8-hour session
/// token, but only if the server actually refuses the session token on that path.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HubTicketTests
{
    private const string TicketEndpoint = "/api/auth/hub-ticket";
    private const string Negotiate = "/hubs/stok/negotiate?negotiateVersion=1";

    private readonly TestDatabaseFixture _db;

    public HubTicketTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The claim set is asserted as a whole, not claim by claim. A ticket that started carrying the
    /// user's e-mail would still pass every "contains sub" style check — the point of a minimal
    /// ticket is what is absent, and only an exact set can state that.
    /// </summary>
    [Fact]
    public async Task Bilet_yalniz_minimum_claimleri_tasiyor_ve_omru_30_saniye()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var token = await ReadTicketAsync(admin);

        Assert.Equal(
            ["aud", "exp", "iss", "jti", "nbf", "role", "scope", "sstamp", "sub"],
            token.Claims.Select(c => c.Type).OrderBy(t => t, StringComparer.Ordinal).ToArray());

        Assert.Equal("hub", Claim(token, "scope"));
        Assert.Equal("Admin", Claim(token, "role"));

        // Exactly thirty seconds. The value matters in both directions: long enough to survive a
        // handshake, short enough that a ticket lifted from a log is worthless by the time it is read.
        Assert.Equal(TimeSpan.FromSeconds(30), token.ValidTo - token.ValidFrom);
    }

    /// <summary>
    /// The role travels so the hub can pick broadcast groups without a database round trip per
    /// connection. Here that is only checked at the ticket level — that a Çalışan's ticket does not
    /// actually reach the admin group needs a live connection and is covered separately.
    /// </summary>
    [Fact]
    public async Task Calisanin_bileti_Admin_rolu_tasimiyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var token = await ReadTicketAsync(calisan);

        Assert.Equal("User", Claim(token, "role"));
        Assert.NotEqual("Admin", Claim(token, "role"));
    }

    [Fact]
    public async Task Biletsiz_negotiate_401_donuyor()
    {
        using var anonymous = _db.Factory.CreateClient();

        var response = await anonymous.PostAsync(Negotiate, null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Both transports, because the JavaScript client uses both on a single connection: the
    /// negotiate POST carries an Authorization header, the WebSocket that follows can only put the
    /// ticket in the query string. Testing one would leave half the handshake unproven.
    /// </summary>
    [Fact]
    public async Task Gecerli_bilet_hem_query_hem_header_ile_negotiate_edebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ticket = await GetTicketAsync(admin);

        using var anonymous = _db.Factory.CreateClient();
        var viaQuery = await anonymous.PostAsync(
            $"{Negotiate}&access_token={Uri.EscapeDataString(ticket)}", null, Ct);
        Assert.Equal(HttpStatusCode.OK, viaQuery.StatusCode);

        using var withHeader = _db.Factory.WithToken(ticket);
        var viaHeader = await withHeader.PostAsync(Negotiate, null, Ct);
        Assert.Equal(HttpStatusCode.OK, viaHeader.StatusCode);
    }

    /// <summary>
    /// The fence, both ways. The second direction is the one with teeth: it is what stops the
    /// 8-hour session token from ever being a valid query-string credential, so it cannot end up in
    /// an access log even if a client decides to put it there. Enforced on the server, not left to
    /// the client's good manners — which is why the query form is tested as well as the header.
    /// </summary>
    [Fact]
    public async Task Ayna_kurali_bilet_RESTte_oturum_tokeni_hubda_reddediliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ticket = await GetTicketAsync(admin);

        using var plain = _db.Factory.CreateClient();
        var sessionToken = await AuthenticatedClient.LoginAsync(
            plain, StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword, Ct);

        using var ticketOnRest = _db.Factory.WithToken(ticket);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ticketOnRest.GetAsync("/api/products", Ct)).StatusCode);

        using var sessionOnHub = _db.Factory.WithToken(sessionToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await sessionOnHub.PostAsync(Negotiate, null, Ct)).StatusCode);

        using var anonymous = _db.Factory.CreateClient();
        var sessionInQuery = await anonymous.PostAsync(
            $"{Negotiate}&access_token={Uri.EscapeDataString(sessionToken)}", null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionInQuery.StatusCode);
    }

    /// <summary>
    /// Expiry, with its own control in the same test. A ticket forged with an expiry five minutes
    /// back is well past the 30-second lifetime plus the 30-second clock skew; the identical claims
    /// signed with a live expiry are accepted. Without the second half, "we got a 401" could mean
    /// the forged claims were wrong all along and the test would pass no matter what expiry did.
    /// </summary>
    [Fact]
    public async Task Suresi_gecmis_bilet_401_ayni_claimler_taze_surede_200()
    {
        var claims = await HubClaimsAsync(StokTakipFactory.AdminEmail, "Admin");

        using var expiredClient = _db.Factory.WithToken(
            TestTokens.Create(claims, expires: DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await expiredClient.PostAsync(Negotiate, null, Ct)).StatusCode);

        using var liveClient = _db.Factory.WithToken(
            TestTokens.Create(claims, expires: DateTime.UtcNow.AddSeconds(30)));
        Assert.Equal(
            HttpStatusCode.OK,
            (await liveClient.PostAsync(Negotiate, null, Ct)).StatusCode);
    }

    /// <summary>
    /// A ticket minted before the account was switched off must stop working the moment it is.
    /// Thirty seconds is short, but "short" is not a security boundary — the per-request
    /// SecurityStamp/IsActive check is, and it has to run on the hub path too, not just on REST.
    /// The ticket is exercised once while the account is live so the later 401 has one explanation.
    /// </summary>
    [Fact]
    public async Task Pasiflestirilen_kullanicinin_onceden_alinmis_bileti_401_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);

        using var target = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);
        var ticket = await GetTicketAsync(target);

        using var withTicket = _db.Factory.WithToken(ticket);
        Assert.Equal(
            HttpStatusCode.OK,
            (await withTicket.PostAsync(Negotiate, null, Ct)).StatusCode);

        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var deactivate = await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new { isActive = false }, Ct);
        deactivate.EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await withTicket.PostAsync(Negotiate, null, Ct)).StatusCode);
    }

    /// <summary>The claims a real ticket carries, rebuilt by hand so expiry can be varied. The
    /// security stamp comes from the database — it is never exposed over HTTP.</summary>
    private async Task<Claim[]> HubClaimsAsync(string email, string role)
    {
        var session = await TestTokens.SessionClaimsAsync(_db, email, role, Ct);

        return
        [
            .. session,
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(StokTakip.Infrastructure.Auth.TokenService.ScopeClaimType,
                StokTakip.Infrastructure.Auth.TokenService.HubScope)
        ];
    }

    private static async Task<string> GetTicketAsync(HttpClient client)
    {
        var response = await client.PostAsync(TicketEndpoint, null, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Ticket>(Ct))!.Token;
    }

    private static async Task<JwtSecurityToken> ReadTicketAsync(HttpClient client)
        => new JwtSecurityTokenHandler().ReadJwtToken(await GetTicketAsync(client));

    private static string? Claim(JwtSecurityToken token, string type)
        => token.Claims.SingleOrDefault(c => c.Type == type)?.Value;

    private sealed record Ticket(string Token, DateTime ExpiresAt);
}
