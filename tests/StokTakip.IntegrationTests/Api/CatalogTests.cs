using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// Categories and suppliers: the configuration half of the catalogue. Reading is open to every
/// signed-in user, writing belongs to the admin, and nothing that products depend on may be
/// deleted out from under them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogTests
{
    // Seed rows, used wherever a test needs something that already exists. Reusing them instead
    // of creating throwaway rows keeps the seed counts T1 pins from moving.
    private const string SeedCategoryName = "Elektronik";
    private const string SeedSupplierName = "Anadolu Elektronik A.Ş.";

    private readonly TestDatabaseFixture _db;

    public CatalogTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_kategori_CRUD_turunu_tamamliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var created = await admin.PostAsJsonAsync(
            "/api/categories", new { name = "T3 Tur Kategori", description = "CRUD turu" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var dto = await created.Content.ReadFromJsonAsync<Category>(Ct);
        Assert.NotNull(dto);
        Assert.Equal(0, dto.ProductCount);

        var list = await admin.GetFromJsonAsync<Category[]>("/api/categories", Ct);
        Assert.Contains(list!, c => c.Id == dto.Id && c.Name == "T3 Tur Kategori");

        var updated = await admin.PutAsJsonAsync(
            $"/api/categories/{dto.Id}", new { name = "T3 Tur Kategori (düzenlendi)", rowVersion = dto.RowVersion }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var afterUpdate = await admin.GetFromJsonAsync<Category>($"/api/categories/{dto.Id}", Ct);
        Assert.Equal("T3 Tur Kategori (düzenlendi)", afterUpdate!.Name);

        var deleted = await admin.DeleteAsync($"/api/categories/{dto.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Gone for good: the round trip leaves the catalogue exactly as it found it.
        var afterDelete = await admin.GetAsync($"/api/categories/{dto.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Admin_tedarikci_CRUD_turunu_tamamliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var created = await admin.PostAsJsonAsync(
            "/api/suppliers",
            new { name = "T3 Tur Tedarikçi", contactEmail = "tur@t3.local", phone = "0212 000 0000", address = "İstanbul" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var dto = await created.Content.ReadFromJsonAsync<Supplier>(Ct);
        Assert.NotNull(dto);
        Assert.True(dto.IsActive);

        var list = await admin.GetFromJsonAsync<Supplier[]>("/api/suppliers", Ct);
        Assert.Contains(list!, s => s.Id == dto.Id);

        var updated = await admin.PutAsJsonAsync(
            $"/api/suppliers/{dto.Id}",
            new { name = "T3 Tur Tedarikçi (düzenlendi)", contactEmail = "tur@t3.local", isActive = false, rowVersion = dto.RowVersion },
            Ct);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var afterUpdate = await admin.GetFromJsonAsync<Supplier>($"/api/suppliers/{dto.Id}", Ct);
        Assert.Equal("T3 Tur Tedarikçi (düzenlendi)", afterUpdate!.Name);
        Assert.False(afterUpdate.IsActive);

        var deleted = await admin.DeleteAsync($"/api/suppliers/{dto.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await admin.GetAsync($"/api/suppliers/{dto.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Ayni_adla_ikinci_kategori_409_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/categories", new { name = SeedCategoryName }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(4, await CountAsync(db => db.Categories.CountAsync(Ct)));
    }

    /// <summary>
    /// Two firms may share a trading name: the Id is the identity, the contact details tell them
    /// apart. The extra row is removed at the end so the seed counts T1 pins stay put.
    /// </summary>
    [Fact]
    public async Task Ayni_adla_ikinci_tedarikci_acilabiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/suppliers", new { name = SeedSupplierName, contactEmail = "baska@t3.local" }, Ct);
        var created = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<Supplier>(Ct)
            : null;

        try
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(2, await CountAsync(db => db.Suppliers.CountAsync(s => s.Name == SeedSupplierName, Ct)));
        }
        finally
        {
            if (created is not null)
                await admin.DeleteAsync($"/api/suppliers/{created.Id}", Ct);
        }
    }

    [Fact]
    public async Task Tedarikci_var_olan_bir_ada_yeniden_adlandirilabiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var response = await admin.PostAsJsonAsync(
            "/api/suppliers", new { name = "T3 Ad Degisimi", contactEmail = "ad@t3.local" }, Ct);
        var created = (await response.Content.ReadFromJsonAsync<Supplier>(Ct))!;

        try
        {
            var renamed = await admin.PutAsJsonAsync(
                $"/api/suppliers/{created.Id}",
                new { name = SeedSupplierName, contactEmail = "ad@t3.local", isActive = true, rowVersion = created.RowVersion },
                Ct);

            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        }
        finally
        {
            await admin.DeleteAsync($"/api/suppliers/{created.Id}", Ct);
        }
    }

    /// <summary>
    /// The refusal has to say how many products are in the way — "silinemez" alone leaves the
    /// admin with no idea what to do next.
    /// </summary>
    [Fact]
    public async Task Urunu_olan_kategori_silinemiyor_ve_mesaj_urun_sayisini_veriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (id, productCount) = await SeedCategoryAsync();

        var response = await admin.DeleteAsync($"/api/categories/{id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(productCount.ToString(), await TitleOfAsync(response));
        Assert.Equal(4, await CountAsync(db => db.Categories.CountAsync(Ct)));
    }

    [Fact]
    public async Task Urunu_olan_tedarikci_silinemiyor_ve_mesaj_urun_sayisini_veriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (id, productCount) = await SeedSupplierAsync();

        var response = await admin.DeleteAsync($"/api/suppliers/{id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(productCount.ToString(), await TitleOfAsync(response));
        Assert.Equal(3, await CountAsync(db => db.Suppliers.CountAsync(Ct)));
    }

    /// <summary>
    /// productCount drives the delete guard and the UI badge alike, and it is computed by a
    /// separate query from the one that lists the rows — so it can drift.
    /// </summary>
    [Fact]
    public async Task ProductCount_gercek_satir_sayisiyla_ortusuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        await using var db = _db.CreateContext();

        var categories = await admin.GetFromJsonAsync<Category[]>("/api/categories", Ct);
        Assert.NotEmpty(categories!);
        foreach (var category in categories!)
        {
            var actual = await db.Products.CountAsync(p => p.CategoryId == category.Id, Ct);
            Assert.Equal(actual, category.ProductCount);
        }

        var suppliers = await admin.GetFromJsonAsync<Supplier[]>("/api/suppliers", Ct);
        Assert.NotEmpty(suppliers!);
        foreach (var supplier in suppliers!)
        {
            var actual = await db.Products.CountAsync(p => p.SupplierId == supplier.Id, Ct);
            Assert.Equal(actual, supplier.ProductCount);
        }

        // Guard: an all-zero response would satisfy the loops above on an empty catalogue.
        Assert.Contains(categories, c => c.ProductCount > 0);
        Assert.Contains(suppliers, s => s.ProductCount > 0);
    }

    [Fact]
    public async Task Calisan_katalogu_okuyabiliyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var categories = await calisan.GetAsync("/api/categories", Ct);
        var suppliers = await calisan.GetAsync("/api/suppliers", Ct);

        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suppliers.StatusCode);

        // Reading means seeing rows, not an empty list behind a 200.
        Assert.NotEmpty((await categories.Content.ReadFromJsonAsync<Category[]>(Ct))!);
        Assert.NotEmpty((await suppliers.Content.ReadFromJsonAsync<Supplier[]>(Ct))!);
    }

    /// <summary>
    /// The matrix in <see cref="AuthorizationMatrixTests"/> already fixes this for every write
    /// endpoint; repeated here with a fully valid body, since a reader's first suspicion is that
    /// the empty body used there is what got rejected.
    /// </summary>
    [Fact]
    public async Task Calisan_kataloga_yazamiyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var category = await calisan.PostAsJsonAsync(
            "/api/categories", new { name = "Çalışan Denemesi" }, Ct);
        var supplier = await calisan.PostAsJsonAsync(
            "/api/suppliers", new { name = "Çalışan Denemesi", contactEmail = "deneme@t3.local" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, category.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supplier.StatusCode);
    }

    /// <summary>
    /// Contact details are redacted on the server, not hidden by the frontend: the employee's
    /// browser never receives them. Both the list and the detail endpoint are checked — one of
    /// them leaking would be enough.
    /// </summary>
    [Fact]
    public async Task Tedarikci_iletisim_bilgileri_calisandan_gizleniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var adminList = await admin.GetFromJsonAsync<Supplier[]>("/api/suppliers", Ct);
        var seen = Assert.Single(adminList!, s => s.Name == SeedSupplierName);
        Assert.NotEmpty(seen.ContactEmail);
        Assert.NotNull(seen.Phone);
        Assert.NotNull(seen.Address);

        var calisanList = await calisan.GetFromJsonAsync<Supplier[]>("/api/suppliers", Ct);
        var redacted = Assert.Single(calisanList!, s => s.Name == SeedSupplierName);
        Assert.Empty(redacted.ContactEmail);
        Assert.Null(redacted.Phone);
        Assert.Null(redacted.Address);

        var detail = await calisan.GetFromJsonAsync<Supplier>($"/api/suppliers/{seen.Id}", Ct);
        Assert.Empty(detail!.ContactEmail);
        Assert.Null(detail.Phone);
        Assert.Null(detail.Address);

        // Everything else is still visible — redaction must not empty the whole row.
        Assert.Equal(SeedSupplierName, redacted.Name);
    }

    private async Task<(int Id, int ProductCount)> SeedCategoryAsync()
    {
        await using var db = _db.CreateContext();

        var id = await db.Categories.Where(c => c.Name == SeedCategoryName).Select(c => c.Id).SingleAsync(Ct);
        var count = await db.Products.CountAsync(p => p.CategoryId == id, Ct);

        Assert.True(count > 0, "Seed kategorisinin urunu olmali; test aksi halde bir sey kanitlamaz.");

        return (id, count);
    }

    private async Task<(int Id, int ProductCount)> SeedSupplierAsync()
    {
        await using var db = _db.CreateContext();

        var id = await db.Suppliers.Where(s => s.Name == SeedSupplierName).Select(s => s.Id).SingleAsync(Ct);
        var count = await db.Products.CountAsync(p => p.SupplierId == id, Ct);

        Assert.True(count > 0, "Seed tedarikcisinin urunu olmali; test aksi halde bir sey kanitlamaz.");

        return (id, count);
    }

    // ── D9c: category-name uniqueness is Turkish-case-insensitive, and names are cleaned ──────
    // Seed has "Gıda". Every variant below looks like it, or differs only in case, so each must be
    // refused — and by the service's own check (its title), not by the database's safety net,
    // which would mean the two disagree on what "the same name" is.

    [Theory]
    [InlineData("GIDA")]
    [InlineData("gıda")]
    [InlineData("  Gıda  ")]
    [InlineData("Gıda​")]
    [InlineData("Gıda ")]
    public async Task Gorunusu_ya_da_harf_buyuklugu_farkli_ayni_kategori_409_aliyor(string name)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync("/api/categories", new { name }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Bu kategori adı zaten kayıtlı", await TitleOfAsync(response));
        Assert.Equal(4, await CountAsync(db => db.Categories.CountAsync(Ct)));
    }

    // ı/i and c/ç are different letters in Turkish, not decorated variants of one another.
    [Theory]
    [InlineData("T3 Kıl", "T3 Kil")]
    [InlineData("T3 Cam", "T3 Çam")]
    public async Task Yalniz_Turkce_harfle_ayrilan_iki_kategori_acilabiliyor(string first, string second)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var created = new List<int>();

        try
        {
            foreach (var name in new[] { first, second })
            {
                var response = await admin.PostAsJsonAsync("/api/categories", new { name }, Ct);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                created.Add((await response.Content.ReadFromJsonAsync<Category>(Ct))!.Id);
            }
        }
        finally
        {
            foreach (var id in created)
                await admin.DeleteAsync($"/api/categories/{id}", Ct);
        }
    }

    [Fact]
    public async Task Kategori_var_olan_bir_ada_harf_buyuklugu_degistirilerek_adlandirilamiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var response = await admin.PostAsJsonAsync("/api/categories", new { name = "T3 Adlandirma" }, Ct);
        var created = (await response.Content.ReadFromJsonAsync<Category>(Ct))!;

        try
        {
            var renamed = await admin.PutAsJsonAsync(
                $"/api/categories/{created.Id}",
                new { name = "ELEKTRONİK", isActive = true, rowVersion = created.RowVersion }, Ct);

            Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
            Assert.Equal("Bu kategori adı zaten kayıtlı", await TitleOfAsync(renamed));
        }
        finally
        {
            await admin.DeleteAsync($"/api/categories/{created.Id}", Ct);
        }
    }

    [Fact]
    public async Task Kategori_adi_temizlenmis_haliyle_kaydediliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/categories", new { name = "  T3 Temiz   Ad​" }, Ct);
        var created = (await response.Content.ReadFromJsonAsync<Category>(Ct))!;

        try
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("T3 Temiz Ad", created.Name);
        }
        finally
        {
            await admin.DeleteAsync($"/api/categories/{created.Id}", Ct);
        }
    }

    // Passes [Required] — a zero-width space is not whitespace to .NET — but is empty once cleaned.
    [Fact]
    public async Task Yalniz_gorunmez_karakterden_olusan_kategori_adi_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync("/api/categories", new { name = "​" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(4, await CountAsync(db => db.Categories.CountAsync(Ct)));
    }

    [Fact]
    public async Task Tedarikci_adi_temizlenmis_haliyle_kaydediliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/suppliers", new { name = "  T3 Temiz   Tedarikçi​", contactEmail = "temiz@t3.local" }, Ct);
        var created = (await response.Content.ReadFromJsonAsync<Supplier>(Ct))!;

        try
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("T3 Temiz Tedarikçi", created.Name);
        }
        finally
        {
            await admin.DeleteAsync($"/api/suppliers/{created.Id}", Ct);
        }
    }

    private async Task<int> CountAsync(Func<Infrastructure.Data.AppDbContext, Task<int>> count)
    {
        await using var db = _db.CreateContext();

        return await count(db);
    }

    private static async Task<string> TitleOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        return document.RootElement.GetProperty("title").GetString() ?? string.Empty;
    }

    // Local shapes on purpose: they pin the JSON contract. Reusing the application's DTOs would
    // let a renamed field move on both sides at once and the test would never notice.
    private sealed record Category(int Id, string Name, string? Description, int ProductCount, uint RowVersion);

    private sealed record Supplier(
        int Id, string Name, string ContactEmail, string? Phone, string? Address, bool IsActive, int ProductCount,
        uint RowVersion);
}
