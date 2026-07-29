using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

[Collection(DatabaseCollection.Name)]
public sealed class LoginTests
{
    private readonly TestDatabaseFixture _db;

    public LoginTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_admin_dogru_sifreyle_200_ve_Admin_rolu_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await LoginAsync(client, StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginBody>(Ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Token);
        // Exactly Admin: an account that also carried "User" would pass a Contains check while
        // showing the employee menu in the UI.
        Assert.Equal(["Admin"], body.User.Roles);
    }

    [Fact]
    public async Task Yanlis_sifre_401_donuyor()
    {
        var client = _db.Factory.CreateClient();

        var response = await LoginAsync(client, StokTakipFactory.AdminEmail, "kesinlikle-yanlis");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Account enumeration guard: if an unknown address answered differently from a wrong
    /// password, anyone could discover which addresses are registered by reading the response.
    /// </summary>
    [Fact]
    public async Task Kayitli_olmayan_eposta_yanlis_sifreyle_birebir_ayni_yaniti_veriyor()
    {
        var client = _db.Factory.CreateClient();

        var unknown = await LoginAsync(client, "kimse@stok.local", "Herhangi!2026");
        var wrongPassword = await LoginAsync(client, StokTakipFactory.AdminEmail, "Herhangi!2026");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var unknownTitle = await TitleOfAsync(unknown);
        var wrongPasswordTitle = await TitleOfAsync(wrongPassword);

        // Guard: comparing two empty strings would pass without proving anything.
        Assert.NotEmpty(unknownTitle);
        Assert.Equal(wrongPasswordTitle, unknownTitle);
    }

    [Fact]
    public async Task Pasiflestirilmis_kullanici_dogru_sifreyle_401_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        var client = _db.Factory.CreateClient();

        // Guard: the same credentials work while the account is active, so the deactivation is
        // demonstrably what closed the door.
        var before = await LoginAsync(client, user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var deactivate = await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var after = await LoginAsync(client, user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private static async Task<string> TitleOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return document.RootElement.GetProperty("title").GetString() ?? string.Empty;
    }

    private sealed record LoginBody(string Token, LoginUser User);

    private sealed record LoginUser(string Id, string Email, string FullName, string[] Roles);
}
