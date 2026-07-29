using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// One rule, five faces: <b>passivity blocks forming a new link, not keeping an existing one.</b>
/// The five tests exist together because the rule is only correct as a set — each one alone
/// reads like an argument for the opposite of one of the others.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PassiveSupplierRuleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public PassiveSupplierRuleTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// The most important test in this file. If the "did the supplier change?" condition in
    /// UpdateAsync is ever "simplified" into an unconditional active-check, a product whose
    /// supplier went inactive later becomes impossible to edit at all — and nothing reports it.
    /// The user simply cannot save, forever, for no stated reason.
    /// </summary>
    [Fact]
    public async Task Pasif_tedarikcili_urun_tedarikcisi_degismeden_duzenlenebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (product, supplierId) = await ProductOnPassiveSupplierAsync(admin, "EDIT");

        var response = await PutAsync(admin, product, name: "T4 Düzenlenmiş Ad", supplierId: supplierId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await admin.GetFromJsonAsync<TestScratch.Product>($"/api/products/{product.Id}", Ct);
        Assert.Equal("T4 Düzenlenmiş Ad", updated!.Name);
    }

    [Fact]
    public async Task Urunun_tedarikcisini_pasif_olana_cevirmek_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var product = await TestScratch.CreateProductAsync(admin, "SWITCH", categoryId, supplierId, Ct);
        var passiveSupplierId = await PassiveSeedSupplierIdAsync();

        var response = await PutAsync(admin, product, name: product.Name, supplierId: passiveSupplierId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The rejected edit changed nothing at all — not even the fields that were fine.
        var unchanged = await admin.GetFromJsonAsync<TestScratch.Product>($"/api/products/{product.Id}", Ct);
        Assert.Equal(supplierId, unchanged!.SupplierId);
    }

    [Fact]
    public async Task Pasif_tedarikciyle_yeni_urun_olusturmak_400_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, _) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var passiveSupplierId = await PassiveSeedSupplierIdAsync();

        var response = await TestScratch.PostProductAsync(admin, "NEWPAS", categoryId, passiveSupplierId, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The movement rule looks at the product, never at its supplier. Stock already on the shelf
    /// has to keep moving after the supplier relationship ends — that is warehouse reality, and
    /// the ledger has to be able to describe it.
    /// </summary>
    [Fact]
    public async Task Pasif_tedarikcili_urune_stok_hareketi_girilebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (product, _) = await ProductOnPassiveSupplierAsync(admin, "MOVE");

        var response = await admin.PostAsJsonAsync(
            "/api/stock-movements",
            new { productId = product.Id, type = "In", quantity = 7, note = "T4 pasif tedarikçi testi" },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = _db.CreateContext();
        Assert.Equal(7, await db.Products.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync(Ct));
    }

    /// <summary>
    /// Passivating a supplier is not deleting it, and must not cascade. Deleting one with
    /// products is refused with 409 (T3); passivating one is allowed precisely because it
    /// preserves everything — that is the path that keeps the audit trail.
    /// </summary>
    [Fact]
    public async Task Urunu_olan_tedarikci_pasiflestirilebiliyor_ve_urunleri_aktif_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (product, supplierId) = await ProductOnPassiveSupplierAsync(admin, "KEEP");

        await using var db = _db.CreateContext();
        Assert.False(await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.IsActive).SingleAsync(Ct));
        Assert.True(await db.Products.Where(p => p.Id == product.Id).Select(p => p.IsActive).SingleAsync(Ct));

        // Still listed as an ordinary active product, not filtered out with its supplier.
        var detail = await admin.GetFromJsonAsync<TestScratch.Product>($"/api/products/{product.Id}", Ct);
        Assert.True(detail!.IsActive);
    }

    /// <summary>
    /// Builds the state the rule is about: an active product whose supplier was passivated after
    /// the link was formed. The passivation itself asserts 204 — every caller depends on it.
    /// </summary>
    private async Task<(TestScratch.Product Product, int SupplierId)> ProductOnPassiveSupplierAsync(
        HttpClient admin, string key)
    {
        var (categoryId, _) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var supplierId = await TestScratch.CreateSupplierAsync(admin, $"Tedarikçi {key}", Ct);
        var product = await TestScratch.CreateProductAsync(admin, key, categoryId, supplierId, Ct);

        await TestScratch.DeactivateSupplierAsync(admin, supplierId, $"Tedarikçi {key}", Ct);

        return (product, supplierId);
    }

    private async Task<int> PassiveSeedSupplierIdAsync()
    {
        await using var db = _db.CreateContext();

        return await db.Suppliers.Where(s => !s.IsActive).Select(s => s.Id).FirstAsync(Ct);
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin, TestScratch.Product product, string name, int supplierId)
        => admin.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            new
            {
                name,
                sku = product.SKU,
                categoryId = product.CategoryId,
                supplierId,
                unitPrice = product.UnitPrice,
                minStockLevel = product.MinStockLevel,
                isActive = product.IsActive,
                rowVersion = product.RowVersion
            },
            Ct);
}
