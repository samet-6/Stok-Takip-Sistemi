using System.Net.Http.Json;
using StokTakip.IntegrationTests.Api.Products;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// "When was this taken out of use" is one question, so it must have one answer everywhere.
/// The stamp is written centrally when IsActive flips, not by each service in turn — these
/// tests exist to catch the day a fourth entity is added and its service forgets to stamp.
/// Categories, suppliers and products are deactivated through three different endpoints on
/// purpose: a shared rule is only shared if every door leads to it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeactivationStampTests : IAsyncDisposable
{
    private readonly TestDatabaseFixture _db;

    public DeactivationStampTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await TestScratch.CleanupAsync(_db, CancellationToken.None);
        await TestUsers.CleanupAsync(_db, CancellationToken.None);
    }

    /// <summary>
    /// The employee case matters most and was the least guarded: "İşten Çıkış" used to be written
    /// by UserService itself. Now it comes from the same rule as the catalogue, and nothing else
    /// in the suite would notice if that rule stopped firing for users.
    /// </summary>
    [Fact]
    public async Task Calisan_isten_cikarilinca_damgalaniyor_geri_alininca_siliniyor()
    {
        var user = await TestUsers.CreateCalisanAsync(_db.Factory, Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        await SetUserActiveAsync(admin, user.Id, false);
        var passive = await FindUserAsync(admin, user.Id);

        Assert.False(passive.IsActive);
        Assert.NotNull(passive.DeactivatedAt);

        await SetUserActiveAsync(admin, user.Id, true);
        var reactivated = await FindUserAsync(admin, user.Id);

        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAt);
    }

    [Fact]
    public async Task Yeni_katalog_kaydi_aktif_ve_damgasiz_dogiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var categoryId = await TestScratch.CreateCategoryAsync(admin, "Damga Dogum", Ct);
        var supplierId = await TestScratch.CreateSupplierAsync(admin, "Damga Dogum", Ct);

        var category = await GetAsync<Lifecycle>(admin, $"/api/categories/{categoryId}");
        var supplier = await GetAsync<Lifecycle>(admin, $"/api/suppliers/{supplierId}");

        Assert.True(category.IsActive);
        Assert.Null(category.DeactivatedAt);
        Assert.True(supplier.IsActive);
        Assert.Null(supplier.DeactivatedAt);
    }

    [Fact]
    public async Task Kategori_pasife_alinip_geri_alininca_damga_dolup_siliniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var id = await TestScratch.CreateCategoryAsync(admin, "Damga Kategori", Ct);

        await SetCategoryActiveAsync(admin, id, false);
        var passive = await GetAsync<Lifecycle>(admin, $"/api/categories/{id}");

        Assert.False(passive.IsActive);
        Assert.NotNull(passive.DeactivatedAt);

        await SetCategoryActiveAsync(admin, id, true);
        var reactivated = await GetAsync<Lifecycle>(admin, $"/api/categories/{id}");

        // Cleared rather than left behind: a stale date would read as "passive since", which is
        // exactly what an active row is not.
        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAt);
    }

    [Fact]
    public async Task Tedarikci_pasife_alinip_geri_alininca_damga_dolup_siliniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var id = await TestScratch.CreateSupplierAsync(admin, "Damga Tedarikci", Ct);

        await SetSupplierActiveAsync(admin, id, false);
        var passive = await GetAsync<Lifecycle>(admin, $"/api/suppliers/{id}");

        Assert.False(passive.IsActive);
        Assert.NotNull(passive.DeactivatedAt);

        await SetSupplierActiveAsync(admin, id, true);
        var reactivated = await GetAsync<Lifecycle>(admin, $"/api/suppliers/{id}");

        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAt);
    }

    [Fact]
    public async Task Urun_pasife_alinip_geri_alininca_damga_dolup_siliniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var product = await TestScratch.CreateProductAsync(admin, "DAMGA-01", categoryId, supplierId, Ct);

        await SetProductActiveAsync(admin, product, false);
        var passive = await GetAsync<Lifecycle>(admin, $"/api/products/{product.Id}");

        Assert.False(passive.IsActive);
        Assert.NotNull(passive.DeactivatedAt);

        await SetProductActiveAsync(admin, product with { RowVersion = passive.RowVersion }, true);
        var reactivated = await GetAsync<Lifecycle>(admin, $"/api/products/{product.Id}");

        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAt);
    }

    private static async Task SetCategoryActiveAsync(HttpClient admin, int id, bool isActive)
    {
        var current = await GetAsync<Lifecycle>(admin, $"/api/categories/{id}");
        var response = await admin.PutAsJsonAsync(
            $"/api/categories/{id}",
            new { name = TestScratch.NamePrefix + "Damga Kategori", isActive, rowVersion = current.RowVersion },
            Ct);

        response.EnsureSuccessStatusCode();
    }

    private static async Task SetSupplierActiveAsync(HttpClient admin, int id, bool isActive)
    {
        var current = await GetAsync<Lifecycle>(admin, $"/api/suppliers/{id}");
        var response = await admin.PutAsJsonAsync(
            $"/api/suppliers/{id}",
            new
            {
                name = TestScratch.NamePrefix + "Damga Tedarikci", contactEmail = "t4@stok.local",
                isActive, rowVersion = current.RowVersion
            },
            Ct);

        response.EnsureSuccessStatusCode();
    }

    private static async Task SetProductActiveAsync(
        HttpClient admin, TestScratch.Product product, bool isActive)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            new
            {
                name = product.Name,
                sku = product.SKU,
                categoryId = product.CategoryId,
                supplierId = product.SupplierId,
                unitPrice = product.UnitPrice,
                minStockLevel = product.MinStockLevel,
                isActive,
                rowVersion = product.RowVersion
            },
            Ct);

        response.EnsureSuccessStatusCode();
    }

    private static async Task SetUserActiveAsync(HttpClient admin, string id, bool isActive)
    {
        var response = await admin.PatchAsJsonAsync($"/api/users/{id}", new { isActive }, Ct);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Employees are read from the list: there is no single-user endpoint.</summary>
    private static async Task<UserRow> FindUserAsync(HttpClient admin, string id)
    {
        var users = await GetAsync<UserRow[]>(admin, "/api/users");

        return Assert.Single(users, u => u.Id == id);
    }

    private static async Task<T> GetAsync<T>(HttpClient admin, string url)
        => (await admin.GetFromJsonAsync<T>(url, Ct))!;

    /// <summary>
    /// The lifecycle slice of all three responses. A local shape, so renaming the JSON field on
    /// both sides at once cannot slip past — and so one record can read three endpoints.
    /// </summary>
    private sealed record Lifecycle(bool IsActive, DateTime? DeactivatedAt, uint RowVersion);

    private sealed record UserRow(string Id, bool IsActive, DateTime? DeactivatedAt);
}
