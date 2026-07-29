using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Employee accounts, managed by the single admin. There is no self-registration, so everything
/// an account can become — created, renamed, re-addressed, reset, dismissed, re-hired — passes
/// through these four endpoints.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UserManagementTests
{
    private const string NewPassword = "T3Yeni!2026";

    private readonly TestDatabaseFixture _db;

    public UserManagementTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_listesinde_yalniz_calisanlar_var_ve_CreatedAt_artan()
    {
        await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var users = await admin.GetFromJsonAsync<UserRow[]>("/api/users", Ct);

        Assert.NotNull(users);
        // The seeded employee proves the list is populated rather than filtered down to nothing.
        Assert.Contains(users, u => u.Email == StokTakipFactory.UserEmail);

        // The admin manages employees, not itself: its own row must not be here to be edited
        // or deactivated by accident.
        Assert.DoesNotContain(users, u => u.Email == StokTakipFactory.AdminEmail);
        Assert.All(users, u => Assert.Equal(["User"], u.Roles));

        // "İşe Giriş" ascending — first hired on top.
        Assert.Equal(
            users.Select(u => u.Id),
            users.OrderBy(u => u.CreatedAt).Select(u => u.Id));
    }

    [Fact]
    public async Task Calisan_kullanici_listesini_goremiyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var response = await calisan.GetAsync("/api/users", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Kullanici_olusturma_201_ve_Calisan_rolu_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var email = $"t3-{Guid.NewGuid():N}@stok.local";

        var response = await admin.PostAsJsonAsync(
            "/api/users", new { fullName = "T3 Yeni Çalışan", email, password = NewPassword }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserRow>(Ct);
        Assert.NotNull(created);
        Assert.Equal(email, created.Email);
        Assert.Equal(["User"], created.Roles);
        Assert.True(created.IsActive);
        Assert.Null(created.DeactivatedAt);

        // The account is usable, not just recorded.
        using var client = await _db.Factory.AsUserAsync(email, NewPassword, Ct);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products", Ct)).StatusCode);
    }

    [Fact]
    public async Task Yinelenen_eposta_ile_kullanici_olusturma_409_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/users",
            new { fullName = "Kopya", email = StokTakipFactory.UserEmail, password = NewPassword },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Zayif_sifreyle_kullanici_olusturma_400_password_alani_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var email = $"t3-{Guid.NewGuid():N}@stok.local";

        var response = await admin.PostAsJsonAsync(
            "/api/users", new { fullName = "Zayıf", email, password = "1234" }, Ct);

        await AssertFieldErrorAsync(response, "password");

        // Rejected means nothing was written — a half-created account without a usable password
        // would be worse than an outright failure.
        await using var db = _db.CreateContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email, Ct));
    }

    [Fact]
    public async Task Kullanici_adini_duzenleme_204_donuyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}", new { fullName = "Yeni Ad Soyad", email = user.Email }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var users = await admin.GetFromJsonAsync<UserRow[]>("/api/users", Ct);
        Assert.Equal("Yeni Ad Soyad", users!.Single(u => u.Id == user.Id).FullName);
    }

    [Fact]
    public async Task Yinelenen_epostaya_duzenleme_409_aliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new { fullName = "T3 Test Çalışanı", email = StokTakipFactory.UserEmail },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // The conflict is pre-checked, so the row must be untouched — not renamed and then failed.
        await using var db = _db.CreateContext();
        Assert.Equal(user.Email, await db.Users.Where(u => u.Id == user.Id).Select(u => u.Email).SingleAsync(Ct));
    }

    /// <summary>
    /// The single seeded admin is out of scope for user management: no endpoint may rename,
    /// re-address or dismiss it, because nothing could restore it afterwards.
    /// </summary>
    [Fact]
    public async Task Admin_hesabi_duzenlenemiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var adminId = await AdminIdAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{adminId}",
            new { fullName = "Ele Geçirildi", email = StokTakipFactory.AdminEmail },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pasif_kullanici_duzenlenemiyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        await SetStatusAsync(admin, user.Id, isActive: false);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}", new { fullName = "Pasifken Düzenleme", email = user.Email }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The reset writes the hash and the new security stamp in one save. An earlier version
    /// wrote in two steps, which could leave an account with no password hash at all — locked
    /// out permanently, with no way back short of a database edit.
    /// </summary>
    [Fact]
    public async Task Sifre_sifirlama_atomik_hash_hicbir_zaman_bos_kalmiyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new { fullName = "T3 Test Çalışanı", email = user.Email, password = NewPassword },
            Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = _db.CreateContext();
        var hash = await db.Users.Where(u => u.Id == user.Id).Select(u => u.PasswordHash).SingleAsync(Ct);
        Assert.False(string.IsNullOrEmpty(hash));

        var client = _db.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email, user.Password)).StatusCode);
    }

    /// <summary>
    /// The account has to move to the new address completely. Identity stores the login name
    /// twice — Email and UserName — and only Email is looked up at login, so a missing UserName
    /// sync stays invisible until someone tries to reuse the freed-up address: the new account
    /// then collides with the old one's stale UserName, and the failure is reported as a
    /// password-policy error, which is where the trail goes cold. Hence the third step.
    /// </summary>
    [Fact]
    public async Task Eposta_degisimi_hesabi_tamamen_yeni_adrese_tasiyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var newEmail = $"t3-yeni-{Guid.NewGuid():N}@stok.local";

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}", new { fullName = "T3 Test Çalışanı", email = newEmail }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var client = _db.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, newEmail, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email, user.Password)).StatusCode);

        // The old address is free again — nothing of the moved account is left holding it.
        var reuse = await admin.PostAsJsonAsync(
            "/api/users",
            new { fullName = "Eski Adresi Devralan", email = user.Email, password = NewPassword },
            Ct);
        Assert.Equal(HttpStatusCode.Created, reuse.StatusCode);
    }

    /// <summary>
    /// Deactivation is a soft delete: the audit trail keeps the movements the employee recorded,
    /// and re-hiring restores access without the admin having to invent a new password.
    /// </summary>
    [Fact]
    public async Task Pasiflestirilip_geri_alinan_kullanici_eski_sifresiyle_girebiliyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var client = _db.Factory.CreateClient();

        await SetStatusAsync(admin, user.Id, isActive: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email, user.Password)).StatusCode);

        await SetStatusAsync(admin, user.Id, isActive: true);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email, user.Password)).StatusCode);

        // "İşten Çıkış" is cleared on re-hire; a lingering date would show the employee as
        // dismissed while they are working.
        var users = await admin.GetFromJsonAsync<UserRow[]>("/api/users", Ct);
        var row = users!.Single(u => u.Id == user.Id);
        Assert.True(row.IsActive);
        Assert.Null(row.DeactivatedAt);
    }

    private static async Task SetStatusAsync(HttpClient admin, string userId, bool isActive)
    {
        var response = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { isActive }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private async Task<string> AdminIdAsync()
    {
        await using var db = _db.CreateContext();

        return await db.Users
            .Where(u => u.Email == StokTakipFactory.AdminEmail)
            .Select(u => u.Id)
            .SingleAsync(Ct);
    }

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");

        Assert.True(errors.TryGetProperty(field, out var messages), $"'{field}' alani beklenmisti.");
        Assert.NotEmpty(messages.EnumerateArray());
    }

    private sealed record UserRow(
        string Id,
        string Email,
        string FullName,
        string[] Roles,
        bool IsActive,
        DateTime CreatedAt,
        DateTime? DeactivatedAt);
}
