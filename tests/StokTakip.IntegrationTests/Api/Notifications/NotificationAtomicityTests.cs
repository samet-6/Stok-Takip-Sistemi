using System.Net;
using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Enums;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// A notification and the movement that caused it share one <c>SaveChangesAsync</c>, which is the
/// entire delivery guarantee of this feature: no outbox, no acknowledgements, because the database
/// <i>is</i> the queue. That only holds if the two really are one write — a notification that
/// survives a rolled-back movement describes an event that never happened, and one that goes
/// missing after a committed movement is a silently dropped alert.
/// <para>
/// The rejection notice is the mirror image and just as clean: a refused movement changes nothing,
/// so there is no transaction to share, and the row must exist exactly when a rejection happened.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationAtomicityTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public NotificationAtomicityTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// Both halves of the atomicity claim in one test, because either alone is satisfied by a
    /// system that never writes notifications at all.
    /// <para>
    /// The rollback is forced the same way T5 forces its passivation race: an open transaction
    /// deactivates the product and holds the row lock, so the movement passes its checks against a
    /// still-active row, stages its notification, blocks on the write, and only then finds the row
    /// moved. The retry reloads, sees a passive product, and refuses — everything staged has to go
    /// with it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Commit_edilen_harekette_bildirim_var_geri_alinanda_hicbiri_yok()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        // Committed: movement row and notification land together.
        var committed = await CreateProductAsync(admin, "ATOM-01", stock: 10, minStockLevel: 5);
        var accepted = await NotificationScratch.TakeOutAsync(admin, committed.Id, 6, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(1, await MovementCountSinceCreationAsync(committed.Id));
        Assert.Single(await NotificationScratch.ForProductAsync(_db, committed.Id, Ct));

        // Rolled back: neither does.
        var rolledBack = await CreateProductAsync(admin, "ATOM-02", stock: 10, minStockLevel: 5);

        await using var blocker = _db.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(Ct);
        var locked = await blocker.Products.SingleAsync(p => p.Id == rolledBack.Id, Ct);
        locked.IsActive = false;
        await blocker.SaveChangesAsync(Ct);

        var pending = NotificationScratch.TakeOutAsync(admin, rolledBack.Id, 6, Ct);
        await Task.Delay(500, Ct);
        await transaction.CommitAsync(Ct);

        var refused = await pending;
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        Assert.Equal(0, await MovementCountSinceCreationAsync(rolledBack.Id));
        Assert.Equal(10, await MovementScratch.StockQuantityAsync(_db, rolledBack.Id, Ct));
        Assert.Empty(await NotificationScratch.ForProductAsync(_db, rolledBack.Id, Ct));
    }

    /// <summary>
    /// The mirror invariant, stated as four assertions because a partial write satisfies any one of
    /// them: the caller is refused, no movement row appears, the stock is untouched, and the
    /// rejection notice <b>is</b> written. The last one is the whole point — the refusal has to
    /// leave a trace even though the transaction it would have joined never existed.
    /// </summary>
    [Fact]
    public async Task Reddedilen_cikis_hicbir_sey_yazmiyor_ama_red_bildirimi_biraktiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "ATOM-03", stock: 4, minStockLevel: 1);

        var response = await NotificationScratch.TakeOutAsync(admin, product.Id, 9, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await MovementCountSinceCreationAsync(product.Id));
        Assert.Equal(4, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));

        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
        Assert.Equal(NotificationType.RejectedOutMovement, notification.Type);
        Assert.Equal(4, notification.Quantity);
        Assert.Equal(9, notification.RequestedQuantity);
    }

    /// <summary>
    /// The rejection notice gets its own <c>SaveChangesAsync</c>, on the same context that was
    /// about to write a movement. If the refused movement were still attached when that save ran,
    /// it would ride along — and the ledger would gain a row nobody accepted, without any error.
    /// </summary>
    [Fact]
    public async Task Reddedilen_hareketten_sonra_defter_invarianti_bozulmuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "ATOM-04", stock: 6, minStockLevel: 1);

        await NotificationScratch.TakeOutAsync(admin, product.Id, 99, Ct);
        (await NotificationScratch.TakeOutAsync(admin, product.Id, 2, Ct)).EnsureSuccessStatusCode();
        await NotificationScratch.TakeOutAsync(admin, product.Id, 99, Ct);

        Assert.Equal(
            await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
            await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(4, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Notification staging lives inside the retry loop, next to the arithmetic it describes. An
    /// attempt that loses the race reloads a different quantity, so a notice staged once outside
    /// the loop would describe a crossing that never happened on the attempt that finally
    /// committed — and, worse, would still be attached when that attempt saved.
    /// <para>
    /// Two parallel movements from 10 with a threshold of 5: whichever order they land in, exactly
    /// one of them crosses. Two rows here would mean an abandoned attempt's notice survived.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Paralel_hareketlerde_tek_LowStock_yaziliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "ATOM-05", stock: 10, minStockLevel: 5);

        var responses = await Task.WhenAll(
            NotificationScratch.TakeOutAsync(admin, product.Id, 6, Ct),
            NotificationScratch.TakeOutAsync(admin, product.Id, 1, Ct));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        Assert.Equal(3, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(
            await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
            await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));

        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));
        Assert.Equal(NotificationType.LowStock, notification.Type);

        // Either order is legitimate — 6 first crosses at 4, 1 first crosses at 3 — so the quantity
        // is pinned to the two values a real crossing can produce, not to one of them.
        Assert.Contains(notification.Quantity, new[] { 3, 4 });
    }

    /// <summary>Movements beyond the opening one written by <c>initialStock</c>.</summary>
    private async Task<int> MovementCountSinceCreationAsync(int productId)
        => await MovementScratch.MovementCountAsync(_db, productId, Ct) - 1;

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int stock, int minStockLevel)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock: stock, minStockLevel: minStockLevel);
    }
}
