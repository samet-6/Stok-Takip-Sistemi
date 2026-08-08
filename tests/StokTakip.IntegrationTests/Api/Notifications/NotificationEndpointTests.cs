using System.Net;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// The three read endpoints and the rules around them. Counters here are global — there is no
/// per-user notification, because the system has exactly one admin — so these tests rely on the
/// table being otherwise empty, which the seed guarantees (T1 pins zero) and the sweep restores.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationEndpointTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public NotificationEndpointTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>Every one of them, not just the list: a role check is per-action, and an endpoint
    /// added later inherits nothing from the ones tested before it — the two delete endpoints are
    /// here for exactly that reason.</summary>
    [Fact]
    public async Task Calisan_bildirim_uclarinin_hepsinde_403_aliyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await calisan.GetAsync("/api/notifications", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await calisan.PostAsync("/api/notifications/1/read", null, Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await calisan.PostAsync("/api/notifications/read-all", null, Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await calisan.DeleteAsync("/api/notifications/1", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await calisan.DeleteAsync("/api/notifications/read", Ct)).StatusCode);
    }

    /// <summary>
    /// Paging bounds and ordering together, because they are the same promise: a page number has
    /// to mean the same thing on every request. The tiebreak on Id is what makes that true —
    /// notifications written milliseconds apart can share a CreatedAt, and without it a row could
    /// appear on two pages or on none.
    /// </summary>
    [Fact]
    public async Task Sayfalama_sinirlari_ve_siralama_deterministik()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "END-01", count: 5);

        var clampedSize = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=999", Ct);
        Assert.Equal(50, clampedSize.PageSize);

        var minimumSize = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=0", Ct);
        Assert.Equal(1, minimumSize.PageSize);

        var clampedPage = await NotificationScratch.GetPageAsync(admin, "page=-3&pageSize=10", Ct);
        Assert.Equal(1, clampedPage.Page);

        // Newest first, and the newest notification is the last one written.
        var all = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(ids[^1], all.Items[0].Id);

        var descending = all.Items.Select(n => n.Id).ToList();
        Assert.Equal(descending.OrderByDescending(id => id).ToList(), descending);

        // Page 1 ∪ page 2 covers the set exactly once — the property paging actually promises.
        var firstPage = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=3", Ct);
        var secondPage = await NotificationScratch.GetPageAsync(admin, "page=2&pageSize=3", Ct);
        var combined = firstPage.Items.Concat(secondPage.Items).Select(n => n.Id).ToList();

        Assert.Equal(5, combined.Distinct().Count());
        Assert.Equal(2, firstPage.TotalPages);
    }

    /// <summary>The badge needs the unread total on every fetch, so it travels with the page
    /// rather than behind a second call that could disagree with it.</summary>
    [Fact]
    public async Task unreadCount_listeyle_birlikte_ve_dogru_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "END-02", count: 3);

        var before = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(3, before.UnreadCount);
        Assert.Equal(3, before.TotalCount);

        (await admin.PostAsync($"/api/notifications/{ids[0]}/read", null, Ct)).EnsureSuccessStatusCode();

        var after = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(2, after.UnreadCount);

        // The total does not move: reading is not deleting.
        Assert.Equal(3, after.TotalCount);
    }

    /// <summary>Two clicks must not produce two answers. Already-read is success, not a
    /// conflict — the client cannot know whether its first request landed.</summary>
    [Fact]
    public async Task Okundu_isaretleme_idempotent()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "END-03", count: 2);

        var first = await admin.PostAsync($"/api/notifications/{ids[0]}/read", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await admin.PostAsync($"/api/notifications/{ids[0]}/read", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(1, page.UnreadCount);

        // The timestamp of the first read stands — a second call must not move it.
        var row = await NotificationScratch.ForProductAsync(_db, await ProductIdAsync(ids[0]), Ct);
        Assert.NotNull(row.Single(n => n.Id == ids[0]).ReadAt);
    }

    [Fact]
    public async Task Olmayan_bildirim_id_si_404_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.PostAsync($"/api/notifications/{int.MaxValue}/read", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task read_all_sonrasi_unreadCount_sifir()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        await CreateNotificationsAsync(admin, "END-04", count: 4);

        var response = await admin.PostAsync("/api/notifications/read-all", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(0, page.UnreadCount);
        Assert.Equal(4, page.TotalCount);
        Assert.All(page.Items, n => Assert.NotNull(n.ReadAt));
    }

    /// <summary>
    /// There is no endpoint that creates a notification, and that is a design decision rather than
    /// an omission: every row is produced by a stock event, so an external writer could only ever
    /// insert something the ledger cannot account for. Deleting one is the opposite case — a user
    /// action on a notice they are done with — which is why the two DELETE routes belong here and
    /// a POST never will. Read from the application's own route table so a new endpoint cannot
    /// slip in unnoticed.
    /// </summary>
    [Fact]
    public async Task Bildirim_olusturma_ucu_yok()
    {
        var routes = _db.Factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items;

        var writeRoutes = routes
            .Where(a => a.AttributeRouteInfo?.Template?.StartsWith("api/notifications") == true)
            .Select(a => $"{a.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods).FirstOrDefault()} {a.AttributeRouteInfo!.Template}")
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "DELETE api/notifications/read",
                "DELETE api/notifications/{id:int}",
                "GET api/notifications",
                "POST api/notifications/read-all",
                "POST api/notifications/{id:int}/read"
            ],
            writeRoutes);
    }

    /// <summary>Produces the requested number of notifications the only way the application allows:
    /// through refused Out movements, one product each so de-duplication does not swallow them.</summary>
    private async Task<List<int>> CreateNotificationsAsync(HttpClient admin, string prefix, int count)
    {
        var ids = new List<int>();

        for (var i = 0; i < count; i++)
        {
            var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);
            var product = await MovementScratch.CreateProductAsync(
                admin, $"{prefix}-{i}", categoryId, supplierId, Ct, initialStock: 1, minStockLevel: 1);

            var refused = await NotificationScratch.TakeOutAsync(admin, product.Id, 50, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

            var written = await NotificationScratch.ForProductAsync(_db, product.Id, Ct);
            ids.Add(Assert.Single(written).Id);
        }

        return ids;
    }

    private async Task<int> ProductIdAsync(int notificationId)
    {
        await using var context = _db.CreateContext();

        return (await context.Notifications.FindAsync([notificationId], Ct))!.ProductId;
    }
}
