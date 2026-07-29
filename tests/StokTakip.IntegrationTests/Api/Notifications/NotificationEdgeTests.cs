using System.Net;
using System.Net.Http.Json;
using StokTakip.Domain.Enums;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// Threshold detection is edge detection, not level detection: a notification marks the moment a
/// product <b>crossed</b> its minimum, not the fact that it is currently below one. The difference
/// only shows up over a sequence — a level check would fire again on every further movement while
/// the stock stayed low, and the bell would fill with copies of the same fact.
/// <para>
/// No extra state is needed for this, which is the elegant part: the transaction that updates
/// StockQuantity is already holding the previous value.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationEdgeTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public NotificationEdgeTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Esigin_ustunde_kalan_hareket_bildirim_uretmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-01", stock: 50, minStockLevel: 40);

        await Out(admin, product.Id, 5);

        Assert.Empty(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
    }

    /// <summary>The recorded quantity is the one after the movement — what the admin needs to see
    /// is where the stock ended up, not where it started.</summary>
    [Fact]
    public async Task Esigi_gecen_hareket_tek_LowStock_uretiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-02", stock: 45, minStockLevel: 40);

        await Out(admin, product.Id, 6);

        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
        Assert.Equal(NotificationType.LowStock, notification.Type);
        Assert.Equal(39, notification.Quantity);
        Assert.Null(notification.RequestedQuantity);
    }

    /// <summary>
    /// The sequence that separates edge detection from level detection. Going back above the
    /// threshold produces nothing, and crossing it a second time produces a second notification —
    /// each crossing once. A level check would have fired on the second movement too.
    /// </summary>
    [Fact]
    public async Task Esigin_ustune_cikis_bildirim_uretmiyor_tekrar_gecis_ikinci_LowStock_uretiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-03", stock: 45, minStockLevel: 40);

        await Out(admin, product.Id, 6);   // 45 → 39, crossing
        Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));

        await In(admin, product.Id, 2);    // 39 → 41, back above
        Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));

        await Out(admin, product.Id, 2);   // 41 → 39, crossing again

        var notifications = await NotificationScratch.ForProductAsync(_db, product.Id, Ct);
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Equal(NotificationType.LowStock, n.Type));
    }

    /// <summary>
    /// The case that actually separates edge detection from level detection, and the one a B0 round
    /// showed was missing: a product already below its minimum moving further down. Under a level
    /// check every subsequent movement would fire again and the bell would fill with copies of a
    /// fact the admin has already seen. The sequences above do not catch this — they all move
    /// across the threshold or back over it, where both readings agree.
    /// </summary>
    [Fact]
    public async Task Esigin_altinda_kalmaya_devam_eden_hareket_yeni_bildirim_uretmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-11", stock: 45, minStockLevel: 40);

        await Out(admin, product.Id, 6);   // 45 → 39, the crossing
        Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));

        await Out(admin, product.Id, 1);   // 39 → 38, still below, no new edge
        await Out(admin, product.Id, 1);   // 38 → 37, likewise

        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
        Assert.Equal(39, notification.Quantity);
    }

    /// <summary>
    /// Zero is also "below minimum", so both rules match at once. OutOfStock wins and is the only
    /// row written: its wording already carries the other one's meaning, and two rows for one event
    /// would read as two problems.
    /// </summary>
    [Fact]
    public async Task Sifira_dusen_hareket_OutOfStock_uretiyor_LowStock_sayisi_artmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-04", stock: 45, minStockLevel: 40);

        await Out(admin, product.Id, 6);   // 45 → 39, one LowStock
        await Out(admin, product.Id, 39);  // 39 → 0

        Assert.Equal(1, await NotificationScratch.CountAsync(_db, product.Id, NotificationType.LowStock, Ct));
        Assert.Equal(1, await NotificationScratch.CountAsync(_db, product.Id, NotificationType.OutOfStock, Ct));

        var outOfStock = (await NotificationScratch.ForProductAsync(_db, product.Id, Ct))
            .Single(n => n.Type == NotificationType.OutOfStock);
        Assert.Equal(0, outOfStock.Quantity);
    }

    /// <summary>
    /// Raising the minimum can leave a product below its threshold without any movement having
    /// happened. No notification: the admin who moved the threshold already knows, and telling them
    /// would turn a deliberate configuration change into an alert about itself.
    /// </summary>
    [Fact]
    public async Task MinStockLevel_i_yukseltmek_bildirim_uretmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-05", stock: 20, minStockLevel: 5);

        var response = await admin.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            new
            {
                name = product.Name,
                sku = product.SKU,
                categoryId = product.CategoryId,
                supplierId = product.SupplierId,
                unitPrice = product.UnitPrice,
                minStockLevel = 100,
                isActive = true,
                rowVersion = product.RowVersion
            },
            Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
    }

    /// <summary>A frozen product is a catalog decision the admin already made, not a sign that
    /// stock is missing — the refusal is not an event worth a notification.</summary>
    [Fact]
    public async Task Pasif_urune_hareket_400_veriyor_ve_bildirim_uretmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-06", stock: 3, minStockLevel: 40);

        await MovementScratch.DeactivateProductAsync(admin, product, Ct);

        // Would have been refused for insufficient stock too, if it had got that far — so a
        // RejectedOutMovement row appearing here would mean the passive check ran second.
        var response = await NotificationScratch.TakeOutAsync(admin, product.Id, 99, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
    }

    // ---- Tekrar-eleme: yalnız reddedilen çıkışlarda ----

    /// <summary>
    /// De-duplication exists because a refusal is the one notification a client can produce on
    /// demand: nothing changes, so a retry loop asking for stock that is not there would write a
    /// row per attempt, and there is no rate limiting anywhere in this project to stop it. One
    /// unread notice per product says everything a second one would.
    /// </summary>
    [Fact]
    public async Task Ayni_urune_ust_uste_reddedilen_cikislar_tek_satir_yaziyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-07", stock: 2, minStockLevel: 1);

        for (var i = 0; i < 4; i++)
        {
            var response = await NotificationScratch.TakeOutAsync(admin, product.Id, 50, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
        Assert.Equal(NotificationType.RejectedOutMovement, notification.Type);
        Assert.Equal(2, notification.Quantity);            // what was on hand
        Assert.Equal(50, notification.RequestedQuantity);  // what was asked for
    }

    /// <summary>
    /// The de-duplication key is "unread", not "exists". Once the admin has seen the notice, the
    /// next refusal is news again — otherwise a product that came up short in January would stay
    /// silent forever.
    /// </summary>
    [Fact]
    public async Task Okundu_isaretlenen_redden_sonra_yeni_red_yeni_satir_yaziyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "NTF-08", stock: 2, minStockLevel: 1);

        await NotificationScratch.TakeOutAsync(admin, product.Id, 50, Ct);
        var first = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));

        var read = await admin.PostAsync($"/api/notifications/{first.Id}/read", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        await NotificationScratch.TakeOutAsync(admin, product.Id, 50, Ct);

        var notifications = await NotificationScratch.ForProductAsync(_db, product.Id, Ct);
        Assert.Equal(2, notifications.Count);
        Assert.NotNull(notifications[0].ReadAt);
        Assert.Null(notifications[1].ReadAt);
    }

    /// <summary>The pending check is scoped to the product — a refusal on one product must not
    /// swallow a refusal on another, which is what a query missing its ProductId filter would do.</summary>
    [Fact]
    public async Task Farkli_urunlerin_redleri_birbirini_elemiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var first = await CreateProductAsync(admin, "NTF-09", stock: 2, minStockLevel: 1);
        var second = await CreateProductAsync(admin, "NTF-10", stock: 2, minStockLevel: 1);

        await NotificationScratch.TakeOutAsync(admin, first.Id, 50, Ct);
        await NotificationScratch.TakeOutAsync(admin, second.Id, 50, Ct);

        Assert.Single(await NotificationScratch.ForProductAsync(_db, first.Id, Ct));
        Assert.Single(await NotificationScratch.ForProductAsync(_db, second.Id, Ct));
    }

    private async Task Out(HttpClient admin, int productId, int quantity)
        => (await NotificationScratch.TakeOutAsync(admin, productId, quantity, Ct))
            .EnsureSuccessStatusCode();

    private async Task In(HttpClient admin, int productId, int quantity)
        => (await NotificationScratch.PutInAsync(admin, productId, quantity, Ct))
            .EnsureSuccessStatusCode();

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int stock, int minStockLevel)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock: stock, minStockLevel: minStockLevel);
    }
}
