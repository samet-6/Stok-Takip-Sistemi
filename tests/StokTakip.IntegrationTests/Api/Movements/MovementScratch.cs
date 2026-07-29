using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// Movement tests write ledger rows, and the ledger is what T1 pins to the digit (22 seeded
/// movements, 0 notifications). Everything created here hangs off a product whose SKU carries
/// the T5 prefix, and the sweep deletes by that prefix rather than by tracked ids — a test that
/// fails halfway still leaves nothing behind.
///
/// Rejected Out movements write a notification even though no movement row lands, so the sweep
/// has to clear notifications too or T1's "0 bildirim" breaks two phases later.
/// </summary>
internal static class MovementScratch
{
    public const string SkuPrefix = "T5-";
    public const string NamePrefix = "T5 ";

    public static string Sku(string suffix) => SkuPrefix + suffix;

    public static async Task CleanupAsync(TestDatabaseFixture db, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        // Case-insensitive: SKUs are upper-cased on the way in, so this never matters in normal
        // operation — but the sweep must not depend on a rule these tests could break.
        var productIds = await context.Products
            .Where(p => p.SKU.ToUpper().StartsWith(SkuPrefix))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (productIds.Count > 0)
        {
            await context.Notifications.Where(n => productIds.Contains(n.ProductId)).ExecuteDeleteAsync(ct);
            await context.StockMovements.Where(m => productIds.Contains(m.ProductId)).ExecuteDeleteAsync(ct);
            await context.Products.Where(p => productIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
        }

        await context.Suppliers.Where(s => s.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync(ct);
        await context.Categories.Where(c => c.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync(ct);
    }

    /// <summary>An active category/supplier pair from the seed, for tests that only need somewhere
    /// to hang a product. Fewer rows to sweep than creating a private pair every time.</summary>
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
            "/api/suppliers", new { name = NamePrefix + name, contactEmail = "t5@stok.local" }, ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IdOnly>(ct))!.Id;
    }

    /// <summary>
    /// Creates a product through the real endpoint. <paramref name="initialStock"/> also writes an
    /// opening In movement, so tests that count rows must measure a baseline rather than assume 0.
    /// </summary>
    public static async Task<Product> CreateProductAsync(
        HttpClient admin,
        string sku,
        int categoryId,
        int supplierId,
        CancellationToken ct,
        int? initialStock = null,
        int minStockLevel = 0)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/products",
            new
            {
                name = NamePrefix + sku,
                sku = Sku(sku),
                categoryId,
                supplierId,
                unitPrice = 100m,
                minStockLevel,
                initialStock
            },
            ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Product>(ct))!;
    }

    public static Task<HttpResponseMessage> PostMovementAsync(
        HttpClient client, int productId, string type, int quantity,
        CancellationToken ct, string? note = null)
        => client.PostAsJsonAsync(
            "/api/stock-movements",
            new { productId, type, quantity, note = note ?? NamePrefix + "hareket" },
            ct);

    /// <summary>Posts a movement and fails loudly if it was refused — for arranging state.</summary>
    public static async Task<MovementResult> AddMovementAsync(
        HttpClient client, int productId, string type, int quantity,
        CancellationToken ct, string? note = null)
    {
        var response = await PostMovementAsync(client, productId, type, quantity, ct, note);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Hareket eklenemedi ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(ct)}");

        return (await response.Content.ReadFromJsonAsync<MovementResult>(ct))!;
    }

    public static async Task DeactivateProductAsync(HttpClient admin, Product product, CancellationToken ct)
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
            ct);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Reads straight from the database, not through the API: the point of most of these
    /// assertions is what actually landed, independent of what a read endpoint chooses to show.</summary>
    public static async Task<int> StockQuantityAsync(TestDatabaseFixture db, int productId, CancellationToken ct)
    {
        await using var context = db.CreateContext();
        return await context.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync(ct);
    }

    public static async Task<int> MovementCountAsync(TestDatabaseFixture db, int productId, CancellationToken ct)
    {
        await using var context = db.CreateContext();
        return await context.StockMovements.CountAsync(m => m.ProductId == productId, ct);
    }

    /// <summary>SUM(In) − SUM(Out) straight from the ledger — the invariant StockQuantity must equal.</summary>
    public static async Task<int> LedgerNetAsync(TestDatabaseFixture db, int productId, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        return await context.StockMovements
            .Where(m => m.ProductId == productId)
            .SumAsync(m => m.Type == Domain.Enums.StockMovementType.In ? m.Quantity : -m.Quantity, ct);
    }

    /// <summary>The message the API puts in ProblemDetails.title (BadRequestException.Message).</summary>
    public static async Task<string?> TitleAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    /// <summary>The machine-readable discriminator, or null when the response carries none.</summary>
    public static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The seeded Çalışan's id, read through the admin endpoint rather than the database:
    /// the tests care about the id the application hands out, not one they looked up themselves.</summary>
    public static async Task<string> SeedCalisanIdAsync(HttpClient admin, CancellationToken ct)
    {
        var users = await admin.GetFromJsonAsync<List<UserRow>>("/api/users", ct);

        return users!.Single(u => u.Email == StokTakipFactory.UserEmail).Id;
    }

    /// <summary>Local shapes rather than the application's DTOs, so a renamed JSON field breaks the
    /// test instead of moving on both sides at once. Enums are strings — that is the wire format.</summary>
    internal sealed record Product(
        int Id,
        string Name,
        string SKU,
        int CategoryId,
        int SupplierId,
        decimal UnitPrice,
        int StockQuantity,
        int MinStockLevel,
        bool IsActive,
        uint RowVersion);

    internal sealed record MovementRow(
        int Id,
        int ProductId,
        string ProductName,
        string Type,
        int Quantity,
        string? Note,
        DateTime CreatedAt,
        string CreatedByUserId,
        string CreatedByFullName);

    internal sealed record MovementResult(MovementRow Movement, int NewStockQuantity);

    internal sealed record MovementPage(
        IReadOnlyList<MovementRow> Items, int Page, int PageSize, int TotalCount);

    private sealed record UserRow(string Id, string Email, string FullName);

    private sealed record IdOnly(int Id);
}
