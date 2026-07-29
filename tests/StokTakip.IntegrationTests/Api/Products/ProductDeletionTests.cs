using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// Deleting a product means two different things depending on whether it has history, and the
/// difference is the whole point: a product that was never moved leaves no trace worth keeping,
/// while one that was is part of the ledger and can only be closed, never erased.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductDeletionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductDeletionTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Hareketsiz_urun_gercekten_siliniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateAsync(admin, "DEL-01");

        var response = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);

        await using var db = _db.CreateContext();
        Assert.False(await db.Products.AnyAsync(p => p.Id == product.Id, Ct));
    }

    [Fact]
    public async Task Hareketli_urun_silinmiyor_pasiflesiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateAsync(admin, "DEL-02", initialStock: 10);

        var response = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);

        // 200 with a body rather than 204: the caller is being told the row survived, and in
        // what state — the two outcomes are distinguishable without a second request.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var soft = await response.Content.ReadFromJsonAsync<TestScratch.Product>(Ct);
        Assert.False(soft!.IsActive);

        await using var db = _db.CreateContext();
        Assert.True(await db.Products.AnyAsync(p => p.Id == product.Id, Ct));
    }

    /// <summary>
    /// Soft delete is not a one-way door — the product can be brought back, which is also the
    /// documented way out of "a passive product's stock is frozen".
    /// </summary>
    [Fact]
    public async Task Pasiflestirilen_urun_geri_aktiflestirilebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateAsync(admin, "DEL-03", initialStock: 10);

        var deleted = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);
        var soft = (await deleted.Content.ReadFromJsonAsync<TestScratch.Product>(Ct))!;

        var response = await PutAsync(admin, soft, isActive: true, rowVersion: soft.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var current = await admin.GetFromJsonAsync<TestScratch.Product>($"/api/products/{product.Id}", Ct);
        Assert.True(current!.IsActive);
        // Reactivation restores availability, not stock: the quantity was never touched.
        Assert.Equal(10, current.StockQuantity);
    }

    /// <summary>
    /// The "Aktif" switch on the product form is a different road from the delete button: the
    /// same movement-free product that DELETE removes for good is only closed by PUT.
    /// </summary>
    [Fact]
    public async Task PUT_ile_pasiflestirme_satiri_silmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateAsync(admin, "DEL-04");

        var response = await PutAsync(admin, product, isActive: false, rowVersion: product.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = _db.CreateContext();
        var row = await db.Products.SingleAsync(p => p.Id == product.Id, Ct);
        Assert.False(row.IsActive);
    }

    /// <summary>
    /// The reason soft delete exists at all. If passivating a product hid its movements, the
    /// ledger would lose entries every time the catalogue was tidied up.
    /// </summary>
    [Fact]
    public async Task Pasiflestirilen_urunun_gecmis_hareketleri_listelenmeye_devam_ediyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateAsync(admin, "DEL-05", initialStock: 10);

        var before = await MovementCountAsync(admin, product.Id);
        Assert.Equal(1, before);

        var deleted = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Equal(before, await MovementCountAsync(admin, product.Id));

        // Also on the product's own page, which is where an audit actually starts.
        var detail = await admin.GetFromJsonAsync<Detail>($"/api/products/{product.Id}", Ct);
        Assert.False(detail!.IsActive);
        Assert.NotEmpty(detail.RecentMovements);
    }

    private async Task<TestScratch.Product> CreateAsync(HttpClient admin, string sku, int? initialStock = null)
    {
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        return await TestScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock: initialStock);
    }

    private static async Task<int> MovementCountAsync(HttpClient admin, int productId)
    {
        var page = await admin.GetFromJsonAsync<MovementPage>(
            $"/api/stock-movements?productId={productId}", Ct);

        return page!.TotalCount;
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin, TestScratch.Product product, bool isActive, uint rowVersion)
        => admin.PutAsJsonAsync(
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
                rowVersion
            },
            Ct);

    private sealed record Detail(int Id, bool IsActive, object[] RecentMovements);

    private sealed record MovementPage(int TotalCount);
}
