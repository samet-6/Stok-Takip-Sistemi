using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// A JWT is valid for eight hours and the server keeps no session list, so "log this account
/// out now" has to work through the token's contents. These tests prove it actually does —
/// otherwise a dismissed employee would keep full access until their token happened to expire.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionInvalidationTests : IAsyncLifetime
{
    private const string ProtectedEndpoint = "/api/products";
    private const string NewPassword = "T2Yeni!2026";

    private readonly TestDatabaseFixture _db;

    public SessionInvalidationTests(TestDatabaseFixture db) => _db = db;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Throwaway accounts go out with the class that made them (O25).</summary>
    public async ValueTask DisposeAsync() => await TestUsers.CleanupAsync(_db, CancellationToken.None);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pasiflestirme_sonrasi_hedefin_eski_tokeni_401_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var target = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);
        await AssertWorksAsync(target);

        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var response = await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new { isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertRevokedAsync(target);
    }

    [Fact]
    public async Task Admin_sifre_sifirlamasi_sonrasi_hedefin_eski_tokeni_401_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var target = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);
        await AssertWorksAsync(target);

        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new { fullName = "T2 Test Çalışanı", email = user.Email, password = NewPassword },
            Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertRevokedAsync(target);
    }

    [Fact]
    public async Task Admin_eposta_degisikligi_sonrasi_hedefin_eski_tokeni_401_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var target = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);
        await AssertWorksAsync(target);

        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            // The new address keeps the "t2-" prefix rather than being prefixed itself: the sweep
            // matches on the front of the address, and "yeni-t2-…" was the one account a full run
            // used to leave behind.
            new { fullName = "T2 Test Çalışanı", email = $"t2-yeni-{user.Email[3..]}" },
            Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertRevokedAsync(target);
    }

    /// <summary>
    /// Changing your own password must revoke every other copy of your session without logging
    /// you out of the tab you are typing in — hence the fresh token in the response.
    /// </summary>
    [Fact]
    public async Task Self_sifre_degistirmede_eski_token_401_yeni_token_200_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var old = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);
        await AssertWorksAsync(old);

        var response = await old.PostAsJsonAsync(
            "/api/account/change-password",
            new { currentPassword = user.Password, newPassword = NewPassword },
            Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ChangePasswordBody>(Ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Token);

        await AssertRevokedAsync(old);

        using var renewed = _db.Factory.WithToken(body.Token);
        await AssertWorksAsync(renewed);
    }

    [Fact]
    public async Task Self_sifre_degistirmede_yeni_sifre_mevcutla_ayniysa_400_newPassword_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/account/change-password",
            new { currentPassword = user.Password, newPassword = user.Password },
            Ct);

        await AssertFieldErrorAsync(response, expected: "newPassword", absent: "currentPassword");

        // The rejection must also be a true no-op: the session that sent it still works.
        await AssertWorksAsync(client);
    }

    [Fact]
    public async Task Self_sifre_degistirmede_mevcut_sifre_yanlissa_400_currentPassword_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var client = await _db.Factory.AsUserAsync(user.Email, user.Password, Ct);

        var response = await client.PostAsJsonAsync(
            "/api/account/change-password",
            new { currentPassword = "T2Yanlis!2026", newPassword = NewPassword },
            Ct);

        await AssertFieldErrorAsync(response, expected: "currentPassword", absent: "newPassword");
        await AssertWorksAsync(client);
    }

    /// <summary>
    /// Guard for every revocation test: without it, a token that never worked in the first
    /// place would make the 401 below look like a successful revocation.
    /// </summary>
    private static async Task AssertWorksAsync(HttpClient client) =>
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ProtectedEndpoint, Ct)).StatusCode);

    private static async Task AssertRevokedAsync(HttpClient client) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ProtectedEndpoint, Ct)).StatusCode);

    /// <summary>
    /// The absent field is what makes this precise: the frontend highlights the input named
    /// here, so reporting the wrong one points the user at the wrong box.
    /// </summary>
    private static async Task AssertFieldErrorAsync(
        HttpResponseMessage response, string expected, string absent)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");

        Assert.True(errors.TryGetProperty(expected, out var messages), $"'{expected}' alani beklenmisti.");
        Assert.NotEmpty(messages.EnumerateArray());
        Assert.False(errors.TryGetProperty(absent, out _), $"'{absent}' alani beklenmiyordu.");
    }

    private sealed record ChangePasswordBody(string Token, DateTime ExpiresAt);
}
