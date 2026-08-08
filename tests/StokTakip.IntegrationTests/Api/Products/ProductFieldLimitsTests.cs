using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// Text limits on the request have to be the column's limits. When they are wider, the value
/// clears both validation layers and dies in the database instead — the caller is told the
/// server failed, when in fact the input was rejected. Description is the case that drifted:
/// the column holds 500 characters, the request claimed 1000.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductFieldLimitsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public ProductFieldLimitsTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>501 characters — one past the column.</summary>
    private static string TooLongDescription() => new('a', 501);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Sinirdan_uzun_aciklamayla_urun_olusturma_400_description_alani_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var sku = TestScratch.Sku("DESC-01");

        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = TestScratch.NamePrefix + "Uzun Açıklama",
                sku,
                description = TooLongDescription(),
                categoryId,
                supplierId,
                unitPrice = 10m,
                minStockLevel = 1
            },
            Ct);

        await AssertFieldErrorAsync(response, "description");

        await using var db = _db.CreateContext();
        Assert.False(await db.Products.AnyAsync(p => p.SKU == sku, Ct));
    }

    [Fact]
    public async Task Sinirdan_uzun_aciklamayla_urun_duzenleme_400_description_alani_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var created = await TestScratch.CreateProductAsync(admin, "DESC-02", categoryId, supplierId, Ct);

        var response = await admin.PutAsJsonAsync(
            $"/api/products/{created.Id}",
            new
            {
                name = created.Name,
                sku = created.SKU,
                description = TooLongDescription(),
                categoryId,
                supplierId,
                unitPrice = created.UnitPrice,
                minStockLevel = created.MinStockLevel,
                isActive = true,
                rowVersion = created.RowVersion
            },
            Ct);

        await AssertFieldErrorAsync(response, "description");

        // The row is untouched: a rejected edit must not have written anything on its way out.
        await using var db = _db.CreateContext();
        Assert.Null(await db.Products.Where(p => p.Id == created.Id).Select(p => p.Description).SingleAsync(Ct));
    }

    /// <summary>The bound is inclusive — 500 characters is a legal description, not an off-by-one.</summary>
    [Fact]
    public async Task Tam_sinirdaki_aciklama_kabul_ediliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await TestScratch.SeedCatalogAsync(_db, Ct);
        var sku = TestScratch.Sku("DESC-03");
        var description = new string('a', 500);

        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = TestScratch.NamePrefix + "Sınırdaki Açıklama",
                sku,
                description,
                categoryId,
                supplierId,
                unitPrice = 10m,
                minStockLevel = 1
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = _db.CreateContext();
        Assert.Equal(
            description,
            await db.Products.Where(p => p.SKU == sku).Select(p => p.Description).SingleAsync(Ct));
    }

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");

        // Case-insensitive: model validation keys by the PascalCase property name, services
        // write camelCase, and the client matches either (formErrors.ts). See UserManagementTests.
        var messages = errors.EnumerateObject()
            .Where(p => string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .ToList();

        Assert.True(messages.Count == 1, $"'{field}' alani beklenmisti.");
        Assert.NotEmpty(messages[0].EnumerateArray());
    }
}
