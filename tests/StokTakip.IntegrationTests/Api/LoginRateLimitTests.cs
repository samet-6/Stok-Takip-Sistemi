using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// The lockout protects an account; this protects the server. A locked account still costs a
/// query and a password hash verification per attempt, and nothing else in this project limits
/// how fast anyone can ask.
/// <para>
/// The limiter is off for the rest of the suite (StokTakipFactory sets RateLimiting:Enabled to
/// false): every class logs in, repeatedly, from one client, and an enabled limiter would cut
/// unrelated tests at random. So this class builds its own host with it switched on — the
/// alternative, a limit high enough not to disturb the suite, would have been a test that proves
/// nothing.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoginRateLimitTests
{
    private readonly TestDatabaseFixture _db;

    public LoginRateLimitTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ardisik_giris_denemeleri_429_ile_kesiliyor()
    {
        using var factory = RateLimitedFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "kimse@stok.local", password = "Kesinlikle!Yanlis2026" },
                Ct);

            statuses.Add(response.StatusCode);
        }

        // The first request must still be served: a limiter that refuses everything would pass a
        // "did we see a 429" check while breaking the application.
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>
    /// Guard for the switch itself: with the limiter off the same burst goes through untouched.
    /// Without this, a broken toggle would look exactly like a working one.
    /// </summary>
    [Fact]
    public async Task Limiter_kapaliyken_ayni_seri_hic_kesilmiyor()
    {
        using var client = _db.Factory.CreateClient();

        for (var i = 0; i < 15; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "kimse-de-yok@stok.local", password = "Kesinlikle!Yanlis2026" },
                Ct);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private WebApplicationFactory<Program> RateLimitedFactory() =>
        _db.Factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Enabled", "true"));
}
