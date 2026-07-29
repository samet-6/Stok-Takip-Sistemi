using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// The dashboard tiles. They are computed by the database over the whole scope rather than from
/// the page the user happens to be looking at — otherwise "toplam stok değeri" would silently
/// mean "value of the ten rows on screen".
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductSummaryTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductSummaryTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// 101 products against a page size capped at 100: if the summary were computed from a
    /// page, this is the count that would expose it.
    /// </summary>
    [Fact]
    public async Task Sayfa_sinirindan_buyuk_kapsamda_totalProducts_tam_sayiyi_veriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriToplu", Ct);
        await InsertAsync(categoryId, count: 101);

        var summary = await SummaryAsync(admin, $"categoryId={categoryId}");

        Assert.Equal(101, summary.TotalProducts);

        // The list, asked for as much as it will give, still stops at 100 — the two numbers
        // disagreeing is exactly the point.
        var page = await admin.GetFromJsonAsync<ProductPage>(
            $"/api/products?categoryId={categoryId}&pageSize=101", Ct);
        Assert.Equal(100, page!.Items.Length);
        Assert.Equal(101, page.TotalCount);
    }

    [Fact]
    public async Task Ozet_sayilari_gercek_degerlerle_birebir_ortusuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriSayac", Ct);

        // Two healthy, one on the threshold, one passive. Every tile has something to report.
        await InsertAsync(categoryId, [
            new Row("SUM-01", Price: 10m, Stock: 100, MinStock: 5, Active: true),
            new Row("SUM-02", Price: 2.50m, Stock: 4, MinStock: 5, Active: true),
            new Row("SUM-03", Price: 1m, Stock: 5, MinStock: 5, Active: true),
            new Row("SUM-04", Price: 1000m, Stock: 3, MinStock: 5, Active: false)
        ]);

        var summary = await SummaryAsync(admin, $"categoryId={categoryId}");

        Assert.Equal(4, summary.TotalProducts);
        Assert.Equal(3, summary.ActiveCount);
        Assert.Equal(1, summary.PassiveCount);
        // Low stock counts active products at or below their minimum — the passive one is not
        // "eksik", it is closed, and mixing the two would keep the warning tile permanently lit.
        Assert.Equal(2, summary.LowStockCount);
        // 10×100 + 2.50×4 + 1×5 = 1015; the passive product's 1000×3 is not part of it.
        Assert.Equal(1015m, summary.TotalStockValue);
    }

    [Fact]
    public async Task TotalStockValue_yalniz_aktif_urunleri_topluyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriDeger", Ct);

        await InsertAsync(categoryId, [
            new Row("VAL-01", Price: 100m, Stock: 2, MinStock: 0, Active: true),
            new Row("VAL-02", Price: 100m, Stock: 2, MinStock: 0, Active: false)
        ]);

        var summary = await SummaryAsync(admin, $"categoryId={categoryId}");

        // 200, not 400: the passive product's stock is frozen, not part of the inventory's worth.
        Assert.Equal(200m, summary.TotalStockValue);
        Assert.Equal(2, summary.TotalProducts);
    }

    /// <summary>
    /// An empty scope produces no group at all in SQL, so the aggregate comes back as nothing.
    /// Zeros, not nulls and not an error — the tiles must still render.
    /// </summary>
    [Fact]
    public async Task Bos_kapsamda_tum_degerler_sifir_ve_hata_yok()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriBos", Ct);

        var summary = await SummaryAsync(admin, $"categoryId={categoryId}");

        Assert.Equal(0, summary.TotalProducts);
        Assert.Equal(0, summary.ActiveCount);
        Assert.Equal(0, summary.PassiveCount);
        Assert.Equal(0, summary.LowStockCount);
        Assert.Equal(0m, summary.TotalStockValue);
    }

    [Fact]
    public async Task Kapsam_parametreleri_ozeti_daraltiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriKapsam", Ct);
        var supplierId = await TestScratch.CreateSupplierAsync(admin, "TedarikciKapsam", Ct);
        var (_, seedSupplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        await InsertAsync(categoryId, [new Row("SCP-01", 10m, 1, 0, true)], supplierId);
        await InsertAsync(categoryId, [new Row("SCP-02", 10m, 1, 0, true)], seedSupplierId);

        var wholeCatalogue = await SummaryAsync(admin, string.Empty);
        var byCategory = await SummaryAsync(admin, $"categoryId={categoryId}");
        var bySupplier = await SummaryAsync(admin, $"supplierId={supplierId}");
        var byBoth = await SummaryAsync(admin, $"categoryId={categoryId}&supplierId={supplierId}");

        Assert.Equal(2, byCategory.TotalProducts);
        Assert.Equal(1, bySupplier.TotalProducts);
        Assert.Equal(1, byBoth.TotalProducts);
        // Guard: without narrowing, the whole catalogue is strictly larger than any scope.
        Assert.True(wholeCatalogue.TotalProducts > byCategory.TotalProducts);
    }

    /// <summary>
    /// The tiles and the table underneath them must describe the same set of products, or the
    /// screen contradicts itself. IncludeInactive is what makes the list's scope match the
    /// summary's, which always counts passive products too.
    /// </summary>
    [Fact]
    public async Task Ozet_ile_liste_ayni_kapsami_tarif_ediyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriEslesme", Ct);

        await InsertAsync(categoryId, [
            new Row("MTC-01", 10m, 1, 0, true),
            new Row("MTC-02", 10m, 1, 0, false)
        ]);

        var summary = await SummaryAsync(admin, $"categoryId={categoryId}");
        var page = await admin.GetFromJsonAsync<ProductPage>(
            $"/api/products?categoryId={categoryId}&includeInactive=true&pageSize=100", Ct);

        Assert.Equal(summary.TotalProducts, page!.TotalCount);

        // Guard: with the default scope the list is narrower, so the equality above is a real
        // match rather than two numbers that happen to agree everywhere.
        var activeOnly = await admin.GetFromJsonAsync<ProductPage>(
            $"/api/products?categoryId={categoryId}&pageSize=100", Ct);
        Assert.NotEqual(summary.TotalProducts, activeOnly!.TotalCount);
    }

    private static async Task<Summary> SummaryAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<Summary>($"/api/products/summary?{query}", Ct))!;

    /// <summary>
    /// Rows go in through the context rather than the API: these tests are about the aggregate
    /// query, and a hundred HTTP round trips would only make them slow.
    /// </summary>
    private async Task InsertAsync(int categoryId, int count)
    {
        var rows = Enumerable.Range(1, count)
            .Select(i => new Row($"BULK-{i:D3}", Price: 1m, Stock: 1, MinStock: 0, Active: true))
            .ToArray();

        await InsertAsync(categoryId, rows);
    }

    private async Task InsertAsync(int categoryId, Row[] rows, int? supplierId = null)
    {
        var (_, seedSupplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        await using var db = _db.CreateContext();

        db.Products.AddRange(rows.Select(r => new Product
        {
            Name = TestScratch.NamePrefix + r.Sku,
            SKU = TestScratch.Sku(r.Sku),
            CategoryId = categoryId,
            SupplierId = supplierId ?? seedSupplierId,
            UnitPrice = r.Price,
            StockQuantity = r.Stock,
            MinStockLevel = r.MinStock,
            IsActive = r.Active
        }));

        await db.SaveChangesAsync(Ct);
    }

    private sealed record Row(string Sku, decimal Price, int Stock, int MinStock, bool Active);

    private sealed record Summary(
        int TotalProducts, int ActiveCount, int PassiveCount, int LowStockCount, decimal TotalStockValue);

    private sealed record ProductPage(object[] Items, int TotalCount);
}
