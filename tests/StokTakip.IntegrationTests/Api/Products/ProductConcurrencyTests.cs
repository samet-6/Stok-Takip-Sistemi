using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// Two admins on the same product. Optimistic concurrency is the only thing standing between
/// them and a lost update — the second save must be refused, not silently win.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductConcurrencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductConcurrencyTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Bayat_rowVersion_ile_PUT_409_ve_concurrency_conflict_kodu_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONC-01");

        // First edit succeeds and moves the token on; the version in hand is now stale.
        var first = await PutAsync(admin, product, "T4 İlk Düzenleme", product.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await PutAsync(admin, product, "T4 İkinci Düzenleme", product.RowVersion);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // The frontend branches on this code, never on the Turkish title.
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal("concurrency_conflict", document.RootElement.GetProperty("code").GetString());

        var current = await admin.GetFromJsonAsync<TestScratch.Product>($"/api/products/{product.Id}", Ct);
        Assert.Equal("T4 İlk Düzenleme", current!.Name);
    }

    /// <summary>
    /// The real race, not a simulation of it: two contexts read the same row, both write. The
    /// second write must find the token moved and fail — and the row must still hold what the
    /// first writer put there. A lost update would leave both callers believing they succeeded.
    /// </summary>
    [Fact]
    public async Task Iki_context_ayni_urunu_yazinca_ikincisi_catisma_aliyor_ve_ilk_deger_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONC-02");

        await using var firstContext = _db.CreateContext();
        await using var secondContext = _db.CreateContext();

        var first = await firstContext.Products.SingleAsync(p => p.Id == product.Id, Ct);
        var second = await secondContext.Products.SingleAsync(p => p.Id == product.Id, Ct);

        first.Name = "T4 İlk Yazan";
        await firstContext.SaveChangesAsync(Ct);

        second.Name = "T4 İkinci Yazan";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync(Ct));

        await using var verify = _db.CreateContext();
        Assert.Equal(
            "T4 İlk Yazan",
            await verify.Products.Where(p => p.Id == product.Id).Select(p => p.Name).SingleAsync(Ct));
    }

    /// <summary>
    /// xmin belongs to the row, not to the fields a form happens to edit. A stock movement
    /// changes StockQuantity and therefore the token, so an edit form opened beforehand is stale
    /// even though nothing it can edit has changed. This is why the frontend's conflict warning
    /// had to become field-aware instead of just shouting "changed".
    /// </summary>
    [Fact]
    public async Task Stok_hareketi_de_rowVersion_u_bayatlatiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONC-03");

        var movement = await admin.PostAsJsonAsync(
            "/api/stock-movements",
            new { productId = product.Id, type = "In", quantity = 3, note = "T4 jeton testi" },
            Ct);
        Assert.Equal(HttpStatusCode.Created, movement.StatusCode);

        // Nothing editable changed — only the quantity, which this form cannot even set.
        var response = await PutAsync(admin, product, product.Name, product.RowVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<TestScratch.Product> CreateProductAsync(HttpClient admin, string sku)
    {
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);

        return await TestScratch.CreateProductAsync(admin, sku, categoryId, supplierId, Ct);
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin, TestScratch.Product product, string name, uint rowVersion)
        => admin.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            new
            {
                name,
                sku = product.SKU,
                categoryId = product.CategoryId,
                supplierId = product.SupplierId,
                unitPrice = product.UnitPrice,
                minStockLevel = product.MinStockLevel,
                isActive = product.IsActive,
                rowVersion
            },
            Ct);
}
