using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

[Collection(DatabaseCollection.Name)]
public sealed class ProductQueryTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductQueryTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// One box in the search field, four columns behind it. Searching by supplier or category
    /// name is the part people forget exists — and the part that breaks silently, because the
    /// query still returns rows, just fewer of them.
    /// </summary>
    [Fact]
    public async Task Arama_ad_SKU_kategori_adi_ve_tedarikci_adi_uzerinden_isabet_ediyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriZeta", Ct);
        var supplierId = await TestScratch.CreateSupplierAsync(admin, "TedarikciZeta", Ct);
        var product = await TestScratch.CreateProductAsync(
            admin, "ZETA-01", categoryId, supplierId, Ct, name: "T4 UrunZeta");

        foreach (var term in new[] { "urunzeta", "zeta-01", "kategorizeta", "tedarikcizeta" })
        {
            var page = await GetAsync(admin, $"search={term}");

            var row = Assert.Single(page.Items);
            Assert.Equal(product.Id, row.Id);
        }

        // Guard: a query that ignored the term would return the whole catalogue and the
        // Single() above would have failed — but a query that returned nothing for every term
        // would fail the same way, so pin the opposite direction too.
        var noMatch = await GetAsync(admin, "search=kesinlikleboylebirseyyok");
        Assert.Empty(noMatch.Items);
    }

    [Fact]
    public async Task CategoryId_ve_supplierId_filtreleri_kapsami_daraltiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriFiltre", Ct);
        var supplierId = await TestScratch.CreateSupplierAsync(admin, "TedarikciFiltre", Ct);
        var (seedCategoryId, seedSupplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var inScope = await TestScratch.CreateProductAsync(admin, "FLT-01", categoryId, supplierId, Ct);
        var otherCategory = await TestScratch.CreateProductAsync(admin, "FLT-02", seedCategoryId, supplierId, Ct);
        var otherSupplier = await TestScratch.CreateProductAsync(admin, "FLT-03", categoryId, seedSupplierId, Ct);

        var byCategory = await GetAsync(admin, $"categoryId={categoryId}&pageSize=100");
        Assert.Equal([inScope.Id, otherSupplier.Id], byCategory.Items.Select(r => r.Id).Order());
        Assert.DoesNotContain(byCategory.Items, r => r.Id == otherCategory.Id);

        var bySupplier = await GetAsync(admin, $"supplierId={supplierId}&pageSize=100");
        Assert.Equal([inScope.Id, otherCategory.Id], bySupplier.Items.Select(r => r.Id).Order());

        // Both together narrow further than either alone.
        var both = await GetAsync(admin, $"categoryId={categoryId}&supplierId={supplierId}&pageSize=100");
        Assert.Equal(inScope.Id, Assert.Single(both.Items).Id);
    }

    [Fact]
    public async Task LowStockOnly_yalniz_esigin_altindakileri_getiriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriEsik", Ct);
        var (_, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var low = await TestScratch.CreateProductAsync(
            admin, "LOW-01", categoryId, supplierId, Ct, minStockLevel: 5, initialStock: 5);
        var healthy = await TestScratch.CreateProductAsync(
            admin, "LOW-02", categoryId, supplierId, Ct, minStockLevel: 5, initialStock: 50);

        var page = await GetAsync(admin, $"categoryId={categoryId}&lowStockOnly=true&pageSize=100");

        // The threshold is inclusive: sitting exactly on the minimum already counts as low.
        Assert.Equal(low.Id, Assert.Single(page.Items).Id);
        Assert.DoesNotContain(page.Items, r => r.Id == healthy.Id);
    }

    [Fact]
    public async Task IncludeInactive_pasif_urunleri_ancak_istenince_getiriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriPasif", Ct);
        var (_, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var active = await TestScratch.CreateProductAsync(admin, "INA-01", categoryId, supplierId, Ct);
        var passive = await TestScratch.CreateProductAsync(admin, "INA-02", categoryId, supplierId, Ct);
        await DeactivateAsync(admin, passive);

        var visible = await GetAsync(admin, $"categoryId={categoryId}&pageSize=100");
        Assert.Equal(active.Id, Assert.Single(visible.Items).Id);

        var all = await GetAsync(admin, $"categoryId={categoryId}&includeInactive=true&pageSize=100");
        Assert.Equal([active.Id, passive.Id], all.Items.Select(r => r.Id).Order());
    }

    /// <summary>
    /// Paging parameters arrive from a URL, so they arrive as anything at all. Every one of
    /// these lands on a usable page instead of an error or a query that asks the database for
    /// a negative offset.
    /// </summary>
    [Theory]
    [InlineData("page=0", 1, 10)]
    [InlineData("page=-1", 1, 10)]
    [InlineData("pageSize=0", 1, 1)]
    [InlineData("pageSize=-5", 1, 1)]
    [InlineData("pageSize=101", 1, 100)]
    public async Task Sayfalama_sinirlari_kullanilabilir_degerlere_cekiliyor(
        string query, int expectedPage, int expectedPageSize)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await GetAsync(admin, query);

        Assert.Equal(expectedPage, page.Page);
        Assert.Equal(expectedPageSize, page.PageSize);
    }

    [Fact]
    public async Task Cok_buyuk_sayfa_numarasi_bos_liste_donuyor_hata_degil()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var page = await GetAsync(admin, "page=9999");

        Assert.Empty(page.Items);
        Assert.Equal(9999, page.Page);
        // The total still describes the whole catalogue — it is not a count of what fits here.
        Assert.True(page.TotalCount > 0);
    }

    /// <summary>
    /// Name alone cannot order rows, because names repeat. Without the id tie-breaker the
    /// database is free to return equal names in a different order per query, and the same row
    /// can appear on page 1 and again on page 2 while another is never shown at all.
    /// </summary>
    [Fact]
    public async Task Ayni_isimli_urunler_sayfalar_arasinda_tekrarlamiyor_ve_kaybolmuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriSira", Ct);
        var (_, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var expected = new List<int>();
        foreach (var suffix in new[] { "SORT-01", "SORT-02", "SORT-03" })
        {
            var created = await TestScratch.CreateProductAsync(
                admin, suffix, categoryId, supplierId, Ct, name: "T4 Aynı İsim");
            expected.Add(created.Id);
        }

        var first = await GetAsync(admin, $"categoryId={categoryId}&pageSize=2&page=1");
        var second = await GetAsync(admin, $"categoryId={categoryId}&pageSize=2&page=2");

        var firstIds = first.Items.Select(r => r.Id).ToArray();
        var secondIds = second.Items.Select(r => r.Id).ToArray();

        Assert.Equal(2, firstIds.Length);
        Assert.Single(secondIds);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.Equal(expected.Order(), firstIds.Concat(secondIds).Order());
    }

    private static async Task<ProductPage> GetAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<ProductPage>($"/api/products?{query}", Ct))!;

    private static async Task DeactivateAsync(HttpClient admin, TestScratch.Product product)
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
                isActive = false,
                rowVersion = product.RowVersion
            },
            Ct);

        response.EnsureSuccessStatusCode();
    }

    private sealed record ProductPage(Row[] Items, int Page, int PageSize, int TotalCount, int TotalPages);

    private sealed record Row(int Id, string Name, string SKU, int CategoryId, int SupplierId, bool IsActive);
}
