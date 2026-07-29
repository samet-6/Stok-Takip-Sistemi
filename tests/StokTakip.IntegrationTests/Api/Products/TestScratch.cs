using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace StokTakip.IntegrationTests.Api.Products;

/// <summary>
/// Product tests need rows of their own — many of them, some deliberately deleted, one test
/// over a hundred. The seed counts T1 pins to the digit live in the same database, so every
/// row created here carries a prefix and is swept away afterwards. Cleaning by prefix rather
/// than by tracked ids means a test that fails halfway still leaves nothing behind.
/// </summary>
internal static class TestScratch
{
    public const string SkuPrefix = "T4-";
    public const string NamePrefix = "T4 ";

    public static string Sku(string suffix) => SkuPrefix + suffix;

    public static async Task CleanupAsync(TestDatabaseFixture db, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        // Case-insensitive on purpose. SKUs are normalised to upper case on the way in, so this
        // never matters in normal operation — but the sweep must not depend on the very rule
        // some of these tests exist to break.
        var productIds = await context.Products
            .Where(p => p.SKU.ToUpper().StartsWith(SkuPrefix))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (productIds.Count > 0)
        {
            // Notifications and movements point at products, so they go first. Notifications
            // matter beyond the foreign key: T1 asserts the seeded database holds none.
            await context.Notifications.Where(n => productIds.Contains(n.ProductId)).ExecuteDeleteAsync(ct);
            await context.StockMovements.Where(m => productIds.Contains(m.ProductId)).ExecuteDeleteAsync(ct);
            await context.Products.Where(p => productIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
        }

        await context.Suppliers.Where(s => s.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync(ct);
        await context.Categories.Where(c => c.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// An active category/supplier pair from the seed. Tests that only need somewhere to hang a
    /// product use these instead of creating their own — fewer rows to sweep, fewer moving parts.
    /// </summary>
    public static async Task<(int CategoryId, int SupplierId)> SeedCatalogAsync(
        TestDatabaseFixture db, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        var categoryId = await context.Categories
            .Where(c => c.Name == "Elektronik").Select(c => c.Id).SingleAsync(ct);
        var supplierId = await context.Suppliers
            .Where(s => s.Name == "Anadolu Elektronik A.Ş." && s.IsActive).Select(s => s.Id).SingleAsync(ct);

        return (categoryId, supplierId);
    }

    public static async Task<int> CreateCategoryAsync(HttpClient admin, string name, CancellationToken ct)
    {
        var response = await admin.PostAsJsonAsync("/api/categories", new { name = NamePrefix + name }, ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IdOnly>(ct))!.Id;
    }

    public static async Task<int> CreateSupplierAsync(HttpClient admin, string name, CancellationToken ct)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/suppliers",
            new { name = NamePrefix + name, contactEmail = "t4@stok.local" },
            ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IdOnly>(ct))!.Id;
    }

    /// <summary>Suppliers are always born active; passivation is a separate edit.</summary>
    public static async Task DeactivateSupplierAsync(
        HttpClient admin, int supplierId, string name, CancellationToken ct)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/suppliers/{supplierId}",
            new { name = NamePrefix + name, contactEmail = "t4@stok.local", isActive = false },
            ct);

        response.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> PostProductAsync(
        HttpClient admin,
        string sku,
        int categoryId,
        int supplierId,
        CancellationToken ct,
        string? name = null,
        decimal unitPrice = 100m,
        int minStockLevel = 5,
        int? initialStock = null)
        => await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = name ?? NamePrefix + sku,
                sku = Sku(sku),
                categoryId,
                supplierId,
                unitPrice,
                minStockLevel,
                initialStock
            },
            ct);

    public static async Task<Product> CreateProductAsync(
        HttpClient admin,
        string sku,
        int categoryId,
        int supplierId,
        CancellationToken ct,
        string? name = null,
        decimal unitPrice = 100m,
        int minStockLevel = 5,
        int? initialStock = null)
    {
        var response = await PostProductAsync(
            admin, sku, categoryId, supplierId, ct, name, unitPrice, minStockLevel, initialStock);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Product>(ct))!;
    }

    /// <summary>
    /// The fields the product tests assert on. A local shape rather than the application's DTO,
    /// so a renamed JSON field breaks the test instead of moving on both sides at once.
    /// </summary>
    internal sealed record Product(
        int Id,
        string Name,
        string SKU,
        int CategoryId,
        int SupplierId,
        decimal UnitPrice,
        int StockQuantity,
        int MinStockLevel,
        decimal StockValue,
        bool IsActive,
        uint RowVersion);

    private sealed record IdOnly(int Id);
}
