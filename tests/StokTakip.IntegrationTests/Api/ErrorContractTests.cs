using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// The error contract itself: which 401 the client got, and which field a 400 blames. The
/// frontend branches on the machine-readable `code` and on the `errors` keys, never on the
/// Turkish titles — so those are what the tests assert.
///
/// Two different 401s exist and they are not interchangeable. An expired or missing session is
/// written by the JwtBearer OnChallenge hook and carries no `code`; a rejected login travels
/// through GlobalExceptionHandler as an UnauthorizedException and carries `code: unauthorized`.
/// Nothing had ever pinned that difference down, and a refactor that funnelled both through one
/// writer would look correct in every existing test.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ErrorContractTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ErrorContractTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestUsers.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Yanlis_sifreyle_giris_401_ve_unauthorized_kodu_donuyor()
    {
        using var client = _db.Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = StokTakipFactory.AdminEmail, password = "Yanlis!2026" },
            Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await CodeAsync(response));
    }

    /// <summary>
    /// The other 401. It is written before any controller runs, so it cannot carry a `code` —
    /// and the axios interceptor uses exactly this difference to tell "your session ended, log
    /// in again" apart from "these credentials are wrong".
    /// </summary>
    [Fact]
    public async Task Tokensiz_istek_401_veriyor_ama_kod_tasimiyor()
    {
        using var client = _db.Factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await CodeAsync(response));
        Assert.Equal("Kimlik doğrulama gerekli.", await TitleAsync(response));
    }

    /// <summary>
    /// Wrong current password and a policy-violating new password both come back as 400, and the
    /// only thing separating them is the field key. Identity reports both through the same failed
    /// IdentityResult, so dropping the PasswordMismatch check would highlight the wrong input —
    /// the user would be told their new password is bad when in fact they mistyped the old one.
    /// </summary>
    [Fact]
    public async Task Yanlis_mevcut_sifre_currentPassword_alanini_isaretliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/account/change-password",
            new { currentPassword = "Bambaska!2026", newPassword = "Yepyeni!2026" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Mevcut şifre hatalı."], await FieldErrorsAsync(response, "currentPassword"));
    }

    [Fact]
    public async Task Politikaya_uymayan_yeni_sifre_newPassword_alanini_isaretliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/account/change-password",
            new { currentPassword = user.Password, newPassword = "kisa" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await FieldErrorsAsync(response, "newPassword"));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await PropertyAsync(response, "code"))?.GetString();

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
        => (await PropertyAsync(response, "title"))?.GetString();

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return document.RootElement.TryGetProperty("errors", out var errors)
            && errors.TryGetProperty(field, out var messages)
                ? messages.EnumerateArray().Select(m => m.GetString()!).ToArray()
                : [];
    }

    /// <summary>Clones the element: the JsonDocument it belongs to is disposed on return.</summary>
    private static async Task<JsonElement?> PropertyAsync(HttpResponseMessage response, string name)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return document.RootElement.TryGetProperty(name, out var value) ? value.Clone() : null;
    }
}
