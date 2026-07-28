using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// The infrastructure's own test: if these fail, no other integration test result means anything.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SmokeTests
{
    private readonly TestDatabaseFixture _db;

    public SmokeTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uygulama_ayaga_kalkiyor_ve_anonim_istek_401_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await client.GetAsync("/api/products", Ct);

        // 401 rather than a connection error is the proof: the host booted, routing resolved and
        // the authentication middleware ran.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_girisi_200_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = StokTakipFactory.AdminEmail,
            password = StokTakipFactory.AdminPassword,
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Yanlis_parola_401_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = StokTakipFactory.AdminEmail,
            password = "kesinlikle-yanlis",
        }, Ct);

        // Guards the smoke test itself: without this, a login endpoint that returned 200 for
        // everything would still make the test above pass.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Seed_kullanicilari_giris_yapabiliyor_ve_token_tasiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        Assert.NotNull(admin.DefaultRequestHeaders.Authorization);
        Assert.NotNull(calisan.DefaultRequestHeaders.Authorization);

        // The employee reaching a protected read proves the token is actually accepted, not just
        // present as a header.
        var response = await calisan.GetAsync("/api/products", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Veritabani_migrate_edilmis_ve_seed_uygulanmis()
    {
        await using var db = _db.CreateContext();

        Assert.True(await db.Database.CanConnectAsync(Ct));
        Assert.Equal(4, await db.Categories.CountAsync(Ct));
        Assert.Equal(3, await db.Suppliers.CountAsync(Ct));
        Assert.Equal(12, await db.Products.CountAsync(Ct));
    }
}
