using System.Net;
using System.Net.Http.Json;
using StokTakip.Application.Common;
using StokTakip.IntegrationTests.Api.Movements;
using StokTakip.IntegrationTests.Api.Realtime;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// How notifications reach the admin, and what survives when nobody is listening.
/// <para>
/// The signal carries nothing — not even a count. The badge number comes back with the refetch,
/// which is also where the Admin-only authorization is re-applied; pushing the number instead
/// would mean trusting a broadcast payload that no endpoint had checked. That the signal reaches
/// admins only is covered in T7b, against a Çalışan connected at the same moment.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationRealtimeTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public NotificationRealtimeTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>An invalidation key with an empty hand: the receiver learns that something changed
    /// and nothing about what.</summary>
    [Fact]
    public async Task NotificationsChanged_sinyali_yuk_tasimiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "RT-01", stock: 10, minStockLevel: 5);

        await using var client = await ConnectAsync(admin);

        (await NotificationScratch.TakeOutAsync(admin, product.Id, 6, Ct)).EnsureSuccessStatusCode();

        var signal = await client.WaitForAsync(RealtimeEvents.NotificationsChanged, Ct);

        Assert.Null(Assert.Single(signal.Arguments));
    }

    /// <summary>
    /// An ordinary movement must not wake every admin's bell. The signal follows the notification
    /// row, not the request — so a movement that crosses nothing announces the product change and
    /// stops there.
    /// </summary>
    [Fact]
    public async Task Bildirim_uretmeyen_hareket_NotificationsChanged_atmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "RT-02", stock: 50, minStockLevel: 5);

        await using var client = await ConnectAsync(admin);

        (await NotificationScratch.TakeOutAsync(admin, product.Id, 5, Ct)).EnsureSuccessStatusCode();

        // The product signal is the control: it proves the request reached the notifier, so the
        // absent notification signal is a decision rather than a delivery that never happened.
        await client.WaitForAsync(RealtimeEvents.ProductChanged, Ct);
        await client.SettleAsync(Ct);

        Assert.DoesNotContain(client.Received, s => s.Target == RealtimeEvents.NotificationsChanged);
    }

    /// <summary>
    /// The signal is a hint, never the delivery mechanism. An admin who was not connected when the
    /// event happened still finds it on the next fetch, because the row was written inside the
    /// business transaction — the database is the queue, so there is nothing to miss.
    /// </summary>
    [Fact]
    public async Task Admin_baglanmamisken_uretilen_bildirim_sonradan_goruluyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "RT-03", stock: 10, minStockLevel: 5);

        // Nobody is listening on the hub at this point.
        (await NotificationScratch.TakeOutAsync(admin, product.Id, 6, Ct)).EnsureSuccessStatusCode();

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);

        Assert.Contains(page.Items, n => n.ProductId == product.Id && n.Type == "LowStock");
        Assert.Equal(1, page.UnreadCount);
    }

    /// <summary>
    /// Restarting the API changes nothing, because no state lives in it. A fresh host is booted
    /// against the same database and asked the same question — same totals, same unread count.
    /// A queue held in memory would answer differently, and that is exactly the design this test
    /// exists to rule out.
    /// </summary>
    [Fact]
    public async Task Sunucu_yeniden_baslatildiginda_sayilar_ayni_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "RT-04", stock: 10, minStockLevel: 5);

        (await NotificationScratch.TakeOutAsync(admin, product.Id, 6, Ct)).EnsureSuccessStatusCode();
        await NotificationScratch.TakeOutAsync(admin, product.Id, 99, Ct);   // refused → second row

        var before = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(2, before.TotalCount);
        Assert.Equal(2, before.UnreadCount);

        await using var restarted = _db.Factory.WithWebHostBuilder(_ => { });
        using var afterAdmin = restarted.CreateClient();
        var token = await AuthenticatedClient.LoginAsync(
            afterAdmin, StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword, Ct);
        afterAdmin.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var after = await NotificationScratch.GetPageAsync(afterAdmin, "page=1&pageSize=50", Ct);

        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.UnreadCount, after.UnreadCount);
        Assert.Equal(
            before.Items.Select(n => n.Id),
            after.Items.Select(n => n.Id));
    }

    /// <summary>
    /// Deleting is a change like any other, so the bell has to hear it — a second admin session
    /// left holding a row that no longer exists is the same staleness the signal exists to prevent.
    /// <para>
    /// Both halves of the rule are here because they are one promise: the signal follows the row,
    /// not the request. The empty bulk delete is checked first and its silence would prove nothing
    /// on its own — the delete that follows is the control, showing the connection was listening
    /// all along.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Silme_sinyali_yalniz_satir_gercekten_gidince_atiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "RT-05", stock: 10, minStockLevel: 5);

        (await NotificationScratch.TakeOutAsync(admin, product.Id, 6, Ct)).EnsureSuccessStatusCode();
        var notification = Assert.Single(await NotificationScratch.ForProductAsync(_db, product.Id, Ct));

        await using var client = await ConnectAsync(admin);

        // Nothing is read, so there is nothing to clear — and nothing to announce.
        var empty = await admin.DeleteAsync("/api/notifications/read", Ct);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        await client.SettleAsync(Ct);
        Assert.DoesNotContain(client.Received, s => s.Target == RealtimeEvents.NotificationsChanged);

        (await admin.DeleteAsync($"/api/notifications/{notification.Id}", Ct)).EnsureSuccessStatusCode();

        var signal = await client.WaitForAsync(RealtimeEvents.NotificationsChanged, Ct);
        Assert.Null(Assert.Single(signal.Arguments));
    }

    /// <summary>
    /// Hard-deleting a product takes its notifications with it, so the bell has to hear about it
    /// too — otherwise the panel keeps offering a notice whose product is gone, and clicking it
    /// lands on a 404. The product signal alone would not do: it is a different query key.
    /// </summary>
    [Fact]
    public async Task Bildirimli_urun_silinince_bildirim_sinyali_de_atiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        // No opening stock → no movement, so DELETE takes the hard path.
        var product = await MovementScratch.CreateProductAsync(
            admin, "RT-06", categoryId, supplierId, Ct, minStockLevel: 5);

        var refused = await NotificationScratch.TakeOutAsync(admin, product.Id, 3, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await using var client = await ConnectAsync(admin);

        var deleted = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await client.WaitForAsync(RealtimeEvents.NotificationsChanged, Ct);
    }

    private async Task<TestHubClient> ConnectAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/auth/hub-ticket", null, Ct);
        response.EnsureSuccessStatusCode();

        var ticket = (await response.Content.ReadFromJsonAsync<Ticket>(Ct))!.Token;

        return await TestHubClient.ConnectAsync(_db.Factory, ticket, Ct);
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int stock, int minStockLevel)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock: stock, minStockLevel: minStockLevel);
    }

    private sealed record Ticket(string Token);
}
