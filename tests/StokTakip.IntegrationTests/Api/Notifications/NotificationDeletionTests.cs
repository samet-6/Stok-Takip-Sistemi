using System.Net;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// Deleting notifications, which is what separates the panel from an archive nobody can tidy.
/// <para>
/// Two doors, and they answer different needs: the × on a row removes one notice the admin is
/// done with, while "okunmuşları sil" clears everything already dealt with in a single click.
/// That pair is deliberately the whole feature — a retention job or a time-based sweep would
/// add a scheduler and time-dependent tests to buy something one click already buys.
/// </para>
/// <para>
/// Counters here are global (there is no per-user notification, because the system has exactly
/// one admin), so these tests rely on the table being otherwise empty — T1 pins the seed at zero
/// and the sweep in <see cref="DisposeAsync"/> restores it.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationDeletionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public NotificationDeletionTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// Both counters move, and that is the point: marking read moves only the unread count, so a
    /// row that leaves <c>totalCount</c> behind is proof the row itself went and not just its flag.
    /// </summary>
    [Fact]
    public async Task Okunmamis_bildirim_silinince_iki_sayac_da_dusuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "DEL-01", count: 3);

        var response = await admin.DeleteAsync($"/api/notifications/{ids[0]}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.UnreadCount);
        Assert.DoesNotContain(page.Items, n => n.Id == ids[0]);

        // Gone from the table, not just hidden by the read endpoint.
        Assert.Equal(2, await NotificationScratch.TotalCountAsync(_db, Ct));
    }

    /// <summary>The mirror image: a read row was never in the unread count, so removing it must
    /// not move that number — the badge stays where it was.</summary>
    [Fact]
    public async Task Okunmus_bildirim_silinince_unreadCount_degismiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "DEL-02", count: 3);

        (await admin.PostAsync($"/api/notifications/{ids[0]}/read", null, Ct)).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync($"/api/notifications/{ids[0]}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.UnreadCount);
    }

    [Fact]
    public async Task Olmayan_bildirim_silme_404_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await admin.DeleteAsync($"/api/notifications/{int.MaxValue}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Deliberately unlike marking read, which answers 204 twice. "Make sure this is read" is a
    /// state the caller can assert repeatedly; "remove this row" is about a resource, and once it
    /// is gone the honest answer is that there is nothing at that address. The bell disables the
    /// button while the request is in flight rather than leaning on a forgiving server.
    /// </summary>
    [Fact]
    public async Task Ayni_bildirim_ikinci_kez_silinince_404()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "DEL-03", count: 1);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/notifications/{ids[0]}", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/notifications/{ids[0]}", Ct)).StatusCode);
    }

    /// <summary>
    /// The bulk door has one job and must not overreach: everything dealt with goes, everything
    /// still waiting stays. An unread row swept away here would be a notice nobody ever saw.
    /// </summary>
    [Fact]
    public async Task Okunmuslari_sil_yalniz_okunmuslari_siliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var ids = await CreateNotificationsAsync(admin, "DEL-04", count: 4);

        (await admin.PostAsync($"/api/notifications/{ids[0]}/read", null, Ct)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/notifications/{ids[2]}/read", null, Ct)).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync("/api/notifications/read", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var page = await NotificationScratch.GetPageAsync(admin, "page=1&pageSize=50", Ct);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.UnreadCount);
        Assert.Equal([ids[3], ids[1]], page.Items.Select(n => n.Id));   // newest first
        Assert.All(page.Items, n => Assert.Null(n.ReadAt));
    }

    /// <summary>Nothing to clear is not an error: the button may be pressed on a panel that
    /// another session already emptied, and the caller's goal is met either way.</summary>
    [Fact]
    public async Task Okunmus_yokken_okunmuslari_sil_204_ve_hicbir_sey_silmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        await CreateNotificationsAsync(admin, "DEL-05", count: 2);

        var response = await admin.DeleteAsync("/api/notifications/read", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(2, await NotificationScratch.TotalCountAsync(_db, Ct));
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
}
