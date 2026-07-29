using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Entities;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// Money is stored as numeric(18,2) and multiplied by the database, never accumulated in
/// floating point. These tests pin what that actually means at the edges.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductMoneyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductMoneyTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// Nothing rejects extra decimals, so the question is what happens to them. The column has
    /// a scale of 2 and the database rounds to it — the value is not truncated, and it is not
    /// stored at full precision to reappear later in a total that does not add up on screen.
    /// </summary>
    [Fact]
    public async Task Ikiden_fazla_ondalikli_fiyat_iki_haneye_yuvarlaniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var created = await TestScratch.CreateProductAsync(
            admin, "MONEY-01", categoryId, supplierId, Ct, unitPrice: 12.3456m);

        Assert.Equal(12.35m, created.UnitPrice);

        await using var db = _db.CreateContext();
        Assert.Equal(
            12.35m,
            await db.Products.Where(p => p.Id == created.Id).Select(p => p.UnitPrice).SingleAsync(Ct));
    }

    [Fact]
    public async Task Negatif_fiyat_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        var response = await TestScratch.PostProductAsync(
            admin, "MONEY-02", categoryId, supplierId, Ct, unitPrice: -1m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = _db.CreateContext();
        Assert.False(await db.Products.AnyAsync(p => p.SKU == TestScratch.Sku("MONEY-02"), Ct));
    }

    /// <summary>
    /// The number that would expose floating point: 99.999,99 × 100.000 across two rows is
    /// nearly twenty billion, far past the range where a double still counts in kuruş. Summed
    /// as numeric it comes back exact.
    /// </summary>
    [Fact]
    public async Task Buyuk_miktar_ve_fiyat_carpimi_kurusuna_kadar_dogru()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var categoryId = await TestScratch.CreateCategoryAsync(admin, "KategoriPara", Ct);
        var (_, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        await using (var db = _db.CreateContext())
        {
            db.Products.AddRange(
                Money("BIG-01", categoryId, supplierId, 99999.99m, 100_000),
                Money("BIG-02", categoryId, supplierId, 0.01m, 3));
            await db.SaveChangesAsync(Ct);
        }

        var summary = await admin.GetFromJsonAsync<Summary>(
            $"/api/products/summary?categoryId={categoryId}", Ct);

        // 99999.99 × 100000 = 9.999.999.000,00 and 0.01 × 3 = 0,03 — the small row is there so
        // a result that quietly lost the kuruş digits cannot pass.
        Assert.Equal(9_999_999_000.03m, summary!.TotalStockValue);

        // The single row agrees with the total to the kuruş — StockValue is multiplied by the
        // database too, so the detail page and the dashboard cannot drift apart.
        var detail = await admin.GetFromJsonAsync<TestScratch.Product>(
            $"/api/products/{await IdOfAsync("BIG-01")}", Ct);
        Assert.Equal(9_999_999_000.00m, detail!.StockValue);
    }

    private async Task<int> IdOfAsync(string sku)
    {
        await using var db = _db.CreateContext();

        return await db.Products.Where(p => p.SKU == TestScratch.Sku(sku)).Select(p => p.Id).SingleAsync(Ct);
    }

    private static Product Money(string sku, int categoryId, int supplierId, decimal price, int stock) => new()
    {
        Name = TestScratch.NamePrefix + sku,
        SKU = TestScratch.Sku(sku),
        CategoryId = categoryId,
        SupplierId = supplierId,
        UnitPrice = price,
        StockQuantity = stock,
        MinStockLevel = 0,
        IsActive = true
    };

    private sealed record Summary(int TotalProducts, decimal TotalStockValue);
}
