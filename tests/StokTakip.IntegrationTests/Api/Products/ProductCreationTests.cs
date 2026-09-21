using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

[Collection(DatabaseCollection.Name)]
public sealed class ProductCreationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductCreationTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// SKUs are normalised on the way in so the catalogue has one spelling per product. Without
    /// it "abc-1" and "ABC-1" would be two rows, and the uniqueness rule would mean nothing.
    /// </summary>
    [Fact]
    public async Task SKU_buyuk_harfe_normalize_ediliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = "T4 Normalizasyon",
                sku = "  t4-norm-01  ",
                categoryId,
                supplierId,
                unitPrice = 10m,
                minStockLevel = 1
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TestScratch.Product>(Ct);
        Assert.Equal("T4-NORM-01", created!.SKU);

        // Stored that way, not merely echoed that way.
        await using var db = _db.CreateContext();
        Assert.Equal("T4-NORM-01", await db.Products.Where(p => p.Id == created.Id).Select(p => p.SKU).SingleAsync(Ct));
    }

    [Fact]
    public async Task Farkli_harf_buyuklugundeki_ayni_SKU_409_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        await TestScratch.CreateProductAsync(admin, "DUP-01", categoryId, supplierId, Ct);

        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = "T4 Kopya",
                sku = "t4-dup-01",
                categoryId,
                supplierId,
                unitPrice = 10m,
                minStockLevel = 1
            },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = _db.CreateContext();
        Assert.Equal(1, await db.Products.CountAsync(p => p.SKU == "T4-DUP-01", Ct));
    }

    [Fact]
    public async Task Olmayan_kategori_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (_, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var response = await TestScratch.PostProductAsync(admin, "NOCAT", 999999, supplierId, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Olmayan_tedarikci_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, _) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var response = await TestScratch.PostProductAsync(admin, "NOSUP", categoryId, 999999, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The opening stock is not a column that gets set — it is a movement, so the ledger explains
    /// the quantity from the product's first second. Both land in one save; a product that came
    /// into existence with stock but no movement would be unexplainable from day one.
    /// </summary>
    [Fact]
    public async Task InitialStock_urun_ve_giris_hareketini_birlikte_olusturuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var created = await TestScratch.CreateProductAsync(
            admin, "INIT-01", categoryId, supplierId, Ct, initialStock: 25);

        Assert.Equal(25, created.StockQuantity);

        await using var db = _db.CreateContext();
        var movement = await db.StockMovements.SingleAsync(m => m.ProductId == created.Id, Ct);
        Assert.Equal(Domain.Enums.StockMovementType.In, movement.Type);
        Assert.Equal(25, movement.Quantity);
        Assert.Equal("Başlangıç stoğu", movement.Note);
    }

    /// <summary>
    /// The creation response is list-shaped: the client puts the new row into the list it is
    /// already looking at, and asks GET /products/{id} when it wants the detail. Built with an
    /// opening stock on purpose — that movement is exactly what a detail-shaped answer would
    /// carry, so the assertion has something real to catch.
    /// </summary>
    [Fact]
    public async Task Create_yaniti_liste_seklinde_detay_alanlari_tasimiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var response = await TestScratch.PostProductAsync(
            admin, "SHAPE-01", categoryId, supplierId, Ct, initialStock: 5);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = body.RootElement;

        // Still the joined list row, not a bare entity.
        Assert.True(root.TryGetProperty("categoryName", out _));
        Assert.True(root.TryGetProperty("supplierName", out _));
        Assert.True(root.TryGetProperty("rowVersion", out _));

        // The detail-only fields, and with them the movements query and the user-name lookup.
        Assert.False(root.TryGetProperty("recentMovements", out _));
        Assert.False(root.TryGetProperty("stockValue", out _));
    }

    [Fact]
    public async Task InitialStock_verilmezse_stok_sifir_ve_hic_hareket_yok()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var created = await TestScratch.CreateProductAsync(admin, "INIT-00", categoryId, supplierId, Ct);

        Assert.Equal(0, created.StockQuantity);

        await using var db = _db.CreateContext();
        Assert.False(await db.StockMovements.AnyAsync(m => m.ProductId == created.Id, Ct));
    }
}
