using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// The list endpoint's filters. Two of them earn their own tests for reasons beyond "does the
/// WHERE clause work": supplier/category narrow through the movement's product rather than the
/// movement itself, and the date range is the only place where a wall clock could sneak into a
/// backend that is supposed to compare instants only.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementFilterTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementFilterTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task productId_filtresi_yalniz_o_urunu_getiriyor_ve_siralama_CreatedAt_DESC()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateOnSeedCatalogAsync(admin, "FILT-01");
        var other = await CreateOnSeedCatalogAsync(admin, "FILT-02");

        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 10, Ct);
        await MovementScratch.AddMovementAsync(admin, product.Id, "Out", 4, Ct);
        var newest = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 2, Ct);
        await MovementScratch.AddMovementAsync(admin, other.Id, "In", 7, Ct);

        var page = await GetAsync(admin, $"productId={product.Id}&pageSize=100");

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, m => Assert.Equal(product.Id, m.ProductId));

        // Newest first, and the tie-break matters: two movements posted milliseconds apart can
        // share a CreatedAt, so ordering falls to Id descending. Asserting only the timestamps
        // would pass on a list whose ties came back in arbitrary order.
        Assert.Equal(newest.Movement.Id, page.Items[0].Id);
        var ids = page.Items.Select(m => m.Id).ToList();
        Assert.Equal(ids.OrderByDescending(id => id).ToList(), ids);
        var timestamps = page.Items.Select(m => m.CreatedAt).ToList();
        Assert.Equal(timestamps.OrderByDescending(t => t).ToList(), timestamps);
    }

    /// <summary>
    /// The paging assertion is the point: totalCount has to count the filtered set, not the table.
    /// A count taken before the filter is applied looks right on page 1 and hands the UI page
    /// numbers that lead nowhere.
    /// </summary>
    [Fact]
    public async Task type_filtresi_yonu_ayiriyor_ve_totalCount_filtreye_gore_sayiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateOnSeedCatalogAsync(admin, "FILT-03");

        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 10, Ct);
        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 10, Ct);
        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 10, Ct);
        await MovementScratch.AddMovementAsync(admin, product.Id, "Out", 1, Ct);
        await MovementScratch.AddMovementAsync(admin, product.Id, "Out", 1, Ct);

        var incoming = await GetAsync(admin, $"productId={product.Id}&type=In&pageSize=2");
        Assert.Equal(3, incoming.TotalCount);
        Assert.Equal(2, incoming.Items.Count);
        Assert.All(incoming.Items, m => Assert.Equal("In", m.Type));

        var outgoing = await GetAsync(admin, $"productId={product.Id}&type=Out&pageSize=100");
        Assert.Equal(2, outgoing.TotalCount);
        Assert.All(outgoing.Items, m => Assert.Equal("Out", m.Type));
    }

    /// <summary>
    /// Neither column exists on StockMovements — both filters join through Product. A private
    /// category and supplier are created so the expected set is exactly one product's movements;
    /// filtering on a seeded supplier would drag in the seed's own ledger.
    /// </summary>
    [Fact]
    public async Task supplierId_ve_categoryId_urun_uzerinden_daraltiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var categoryId = await MovementScratch.CreateCategoryAsync(admin, "Filtre Kategori", Ct);
        var supplierId = await MovementScratch.CreateSupplierAsync(admin, "Filtre Tedarikçi", Ct);
        var mine = await MovementScratch.CreateProductAsync(admin, "FILT-04", categoryId, supplierId, Ct);
        var other = await CreateOnSeedCatalogAsync(admin, "FILT-05");

        var expected = await MovementScratch.AddMovementAsync(admin, mine.Id, "In", 5, Ct);
        var unexpected = await MovementScratch.AddMovementAsync(admin, other.Id, "In", 5, Ct);

        var bySupplier = await GetAsync(admin, $"supplierId={supplierId}&pageSize=100");
        Assert.Contains(bySupplier.Items, m => m.Id == expected.Movement.Id);
        Assert.DoesNotContain(bySupplier.Items, m => m.Id == unexpected.Movement.Id);
        Assert.All(bySupplier.Items, m => Assert.Equal(mine.Id, m.ProductId));

        var byCategory = await GetAsync(admin, $"categoryId={categoryId}&pageSize=100");
        Assert.Contains(byCategory.Items, m => m.Id == expected.Movement.Id);
        Assert.DoesNotContain(byCategory.Items, m => m.Id == unexpected.Movement.Id);
        Assert.All(byCategory.Items, m => Assert.Equal(mine.Id, m.ProductId));
    }

    /// <summary>
    /// The frontend sends the viewer's local calendar day as offset-aware ISO boundaries; the
    /// backend stores UTC instants and never learns the viewer's timezone. This test walks that
    /// path: today's local day must contain movements posted a moment ago, and yesterday's must
    /// not — the second half is what fails if the boundaries are compared as bare wall clocks.
    /// </summary>
    [Fact]
    public async Task Tarih_araligi_yerel_takvim_gununu_dogru_kapsiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateOnSeedCatalogAsync(admin, "FILT-06");
        var movement = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 3, Ct);

        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(now.Date, now.Offset);
        var dayEnd = dayStart.AddDays(1).AddTicks(-1);

        var today = await GetAsync(
            admin, $"productId={product.Id}&from={Iso(dayStart)}&to={Iso(dayEnd)}&pageSize=100");
        Assert.Contains(today.Items, m => m.Id == movement.Movement.Id);

        // Both neighbouring days, because each one exercises a different boundary: yesterday can
        // only be emptied by `to`, tomorrow only by `from`. A B0 round proved the point — disabling
        // `from` left this test green while it checked yesterday alone.
        var yesterday = await GetAsync(
            admin,
            $"productId={product.Id}&from={Iso(dayStart.AddDays(-1))}&to={Iso(dayStart.AddTicks(-1))}&pageSize=100");
        Assert.Empty(yesterday.Items);

        var tomorrow = await GetAsync(
            admin,
            $"productId={product.Id}&from={Iso(dayEnd.AddTicks(1))}&to={Iso(dayEnd.AddDays(1))}&pageSize=100");
        Assert.Empty(tomorrow.Items);
    }

    /// <summary>
    /// Timezone independence, proven by equivalence rather than by restarting the process under a
    /// different TZ (.NET on Windows ignores the TZ environment variable, and TimeZoneInfo.Local
    /// cannot be reassigned in-process). The same instant is sent twice with offsets eight hours
    /// apart: as text the two boundaries look like different times of day, so a server that read
    /// the wall clock and dropped the offset would answer differently. Identical, non-empty result
    /// sets mean only the instant was ever compared.
    ///
    /// The boundary sits between two movements rather than before both, so each form has to return
    /// a strict subset. Equality alone is a weak claim — two identical full lists satisfy it just as
    /// well, which is exactly how an earlier version of this test survived having `from` disabled.
    /// </summary>
    [Fact]
    public async Task Ayni_an_farkli_offsetlerle_gonderildiginde_ayni_kumeyi_getiriyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateOnSeedCatalogAsync(admin, "FILT-07");

        var older = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 3, Ct);
        await Task.Delay(100, Ct);
        var newer = await MovementScratch.AddMovementAsync(admin, product.Id, "Out", 1, Ct);

        Assert.True(
            older.Movement.CreatedAt < newer.Movement.CreatedAt,
            "İki hareket aynı zaman damgasını aldı — sınır testi anlamsızlaşır.");

        // A millisecond back, because the timestamp in the response is the in-memory value and
        // PostgreSQL stores it truncated to microseconds. Boundary-equal would otherwise be a coin
        // flip; the gap above is a hundred milliseconds, so nothing else moves across.
        var cut = new DateTimeOffset(
            DateTime.SpecifyKind(newer.Movement.CreatedAt, DateTimeKind.Utc)).AddMilliseconds(-1);

        var first = await GetAsync(
            admin, $"productId={product.Id}&from={Iso(cut.ToOffset(TimeSpan.FromHours(3)))}&pageSize=100");
        var second = await GetAsync(
            admin, $"productId={product.Id}&from={Iso(cut.ToOffset(TimeSpan.FromHours(-5)))}&pageSize=100");

        foreach (var page in new[] { first, second })
        {
            Assert.Contains(page.Items, m => m.Id == newer.Movement.Id);
            Assert.DoesNotContain(page.Items, m => m.Id == older.Movement.Id);
        }

        Assert.Equal(
            first.Items.Select(m => m.Id).OrderBy(id => id),
            second.Items.Select(m => m.Id).OrderBy(id => id));
    }

    /// <summary>Round-trip format keeps the offset, and the '+' has to survive the query string —
    /// unescaped it decodes as a space and the boundary silently becomes offset-less.</summary>
    private static string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private async Task<MovementScratch.MovementPage> GetAsync(HttpClient client, string query)
        => (await client.GetFromJsonAsync<MovementScratch.MovementPage>(
            $"/api/stock-movements?{query}", Ct))!;

    private async Task<MovementScratch.Product> CreateOnSeedCatalogAsync(HttpClient admin, string sku)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(admin, sku, categoryId, supplierId, Ct);
    }
}
