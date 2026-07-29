using System.Net;
using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// The retry loop in StockMovementService, exercised the only way it can be: with genuinely
/// parallel HTTP requests. Sequential calls never reach that code — each one reads a committed
/// row, writes it, and the token is never stale. Every test here fires its requests at once and
/// awaits them together.
///
/// Two invariants run through all of them. The ledger and StockQuantity must agree, and a request
/// that was refused must have written nothing — the retry re-inserts a movement whose Id is still
/// 0, so a bug here shows up as duplicate rows rather than as an error.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementConcurrencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementConcurrencyTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Iki_paralel_In_ikisi_de_basarili_stok_tam_iki_artiyor_ve_iki_satir_yaziliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-01", initialStock: 5);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var responses = await BurstAsync(admin, product.Id, "In", 1, count: 2);

        // Both must succeed: a lost race is not an error here, it is a reload and a retry. Two
        // competitors can cost a request at most one attempt each, so the three-attempt budget
        // cannot run out — this is deterministic, not lucky.
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(7, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore + 2, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    [Fact]
    public async Task Uc_paralel_In_ucu_de_basarili_ve_stok_tam_uc_artiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-02", initialStock: 5);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var responses = await BurstAsync(admin, product.Id, "In", 1, count: 3);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(8, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore + 3, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The retry re-adds the same StockMovement instance rather than building a new one, which is
    /// safe only because the failed SaveChanges rolled its whole transaction back and left the Id
    /// at 0. If it ever stopped being safe, the stock arithmetic would still look right — the extra
    /// rows are the only visible symptom, so the row count is the assertion that matters.
    /// </summary>
    [Fact]
    public async Task Paralel_istek_sonrasi_hareket_satiri_sayisi_basarili_istek_sayisina_esit()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-03", initialStock: 5);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var responses = await BurstAsync(admin, product.Id, "In", 1, count: 5);
        var accepted = responses.Count(r => r.IsSuccessStatusCode);

        await AssertOnlyConcurrencyFailuresAsync(responses);
        Assert.Equal(rowsBefore + accepted, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(5 + accepted, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// A retry is not a blind replay: after the reload the rules are judged again against the
    /// committed quantity, so an Out that was valid when it arrived can become invalid. The refused
    /// one must come back as 400 "Yetersiz stok", not 409 — the caller's request was answered, not
    /// deferred, and re-sending it would fail the same way.
    /// </summary>
    [Fact]
    public async Task Stok_1_iken_iki_paralel_Out_birini_kabul_edip_digerine_yetersiz_stok_diyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-04", initialStock: 1);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var responses = await BurstAsync(admin, product.Id, "Out", 1, count: 2);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));

        var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.StartsWith("Yetersiz stok", await MovementScratch.TitleAsync(refused, Ct));

        Assert.Equal(0, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore + 1, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The same contract as the test above, but with the collision forced instead of hoped for —
    /// added after a B0 round showed the parallel version does not reliably reach the retry path.
    /// Removing the re-check from the catch block left that test green, because the second request
    /// usually reads the already-committed zero on its first attempt and never collides at all.
    ///
    /// Here an open transaction drains the stock and holds the row lock, so the request provably
    /// passes its first check against a stock of 1, blocks on the write, and only then finds the
    /// row moved. The 400 it comes back with can only have been decided after the reload.
    /// </summary>
    [Fact]
    public async Task Catisma_sonrasi_kurallar_taze_stokla_yeniden_yargilaniyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-08", initialStock: 1);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        await using var blocker = _db.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(Ct);

        // A real ledger entry, not just a hand-written quantity: the row this test leaves behind has
        // to satisfy the same invariant everything else asserts.
        var locked = await blocker.Products.SingleAsync(p => p.Id == product.Id, Ct);
        locked.StockQuantity = 0;
        blocker.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Type = StockMovementType.Out,
            Quantity = 1,
            Note = MovementScratch.NamePrefix + "kilit",
            CreatedByUserId = await blocker.Users.Select(u => u.Id).FirstAsync(Ct)
        });
        await blocker.SaveChangesAsync(Ct);

        var pending = MovementScratch.PostMovementAsync(admin, product.Id, "Out", 1, Ct);
        await Task.Delay(500, Ct);
        await transaction.CommitAsync(Ct);

        var response = await pending;

        // 400, not 409: the request was answered, not deferred. The quantity in the message is the
        // reloaded one, so it doubles as proof of which value the rule was judged against.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Yetersiz stok. Mevcut: 0", await MovementScratch.TitleAsync(response, Ct));

        Assert.Equal(0, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore + 1, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(
            await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
            await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The passivation race, forced deterministically instead of hoped for. An open transaction
    /// deactivates the product and holds the row lock; the movement request then reads the row as
    /// still active (the change is uncommitted and invisible), passes the check, and blocks on the
    /// lock when it tries to write. Committing releases it into a stale token, which sends it round
    /// the retry path — where the reload finally shows a passive product.
    ///
    /// If the request happens to arrive after the commit instead, it simply reads the passive row
    /// on its first attempt. Both paths end in the same 400 with the same message, so the test does
    /// not depend on winning the timing.
    /// </summary>
    [Fact]
    public async Task Hareket_yazilirken_urun_pasiflestirilirse_400_urun_pasif_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-05", initialStock: 10);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        await using var blocker = _db.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(Ct);

        var locked = await blocker.Products.SingleAsync(p => p.Id == product.Id, Ct);
        locked.IsActive = false;
        await blocker.SaveChangesAsync(Ct);

        var pending = MovementScratch.PostMovementAsync(admin, product.Id, "In", 1, Ct);
        await Task.Delay(500, Ct);
        await transaction.CommitAsync(Ct);

        var response = await pending;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Ürün pasif; stok hareketi için önce ürünü aktifleştirin.",
            await MovementScratch.TitleAsync(response, Ct));
        Assert.Equal(10, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Ten at once is past what a three-attempt budget can absorb, so some requests are expected to
    /// exhaust it and come back 409. The test does not require that any of them do — which request
    /// loses is up to the scheduler, and asserting "at least one 409" would be a flaky way of
    /// stating something weaker. What must hold either way is that a refusal wrote nothing: stock
    /// and the row count both follow the number of accepted requests exactly.
    /// </summary>
    [Fact]
    public async Task Butce_tukenen_istekler_409_alsa_da_stok_tutarli_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-06", initialStock: 5);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var responses = await BurstAsync(admin, product.Id, "In", 1, count: 10);
        var accepted = responses.Count(r => r.IsSuccessStatusCode);

        await AssertOnlyConcurrencyFailuresAsync(responses);
        Assert.Equal(5 + accepted, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(rowsBefore + accepted, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(
            await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
            await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The invariant the concurrency token exists to protect, under mixed parallel load. Drop the
    /// token and every one of the tests above still passes on the happy path — each request would
    /// simply write its own arithmetic on top of a value it read before someone else changed it,
    /// and StockQuantity would drift away from the ledger without a single error being raised.
    /// This is the assertion that notices.
    /// </summary>
    [Fact]
    public async Task Karisik_paralel_yuk_sonrasi_stok_hareket_netine_esit()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "PAR-07", initialStock: 200);

        var plan = new (string Type, int Quantity)[]
        {
            ("In", 3), ("Out", 2), ("In", 5), ("Out", 4),
            ("In", 1), ("Out", 6), ("In", 2), ("Out", 1)
        };

        var tasks = plan
            .Select(p => MovementScratch.PostMovementAsync(admin, product.Id, p.Type, p.Quantity, Ct))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        await AssertOnlyConcurrencyFailuresAsync(responses);

        // Two independent readings of the same number: the column, and the sum of the rows.
        var stock = await MovementScratch.StockQuantityAsync(_db, product.Id, Ct);
        Assert.Equal(await MovementScratch.LedgerNetAsync(_db, product.Id, Ct), stock);

        // And the column also has to match what the accepted requests actually asked for, so a
        // ledger that lost rows in step with the column cannot pass.
        var expected = 200 + plan
            .Where((_, i) => responses[i].IsSuccessStatusCode)
            .Sum(p => p.Type == "In" ? p.Quantity : -p.Quantity);
        Assert.Equal(expected, stock);
    }

    /// <summary>Every request either succeeded or was refused for contention — nothing else. A 500
    /// or a stray 400 would otherwise hide inside the "not accepted" bucket the counts allow for.</summary>
    private static async Task AssertOnlyConcurrencyFailuresAsync(IReadOnlyList<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            if (response.IsSuccessStatusCode) continue;

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("concurrency_conflict", await MovementScratch.CodeAsync(response, Ct));
        }
    }

    /// <summary>Starts every request before awaiting any of them — the whole point. Awaiting inside
    /// the loop would make this a sequential test that never touches the retry path.</summary>
    private static async Task<IReadOnlyList<HttpResponseMessage>> BurstAsync(
        HttpClient client, int productId, string type, int quantity, int count)
    {
        var tasks = Enumerable.Range(0, count)
            .Select(_ => MovementScratch.PostMovementAsync(client, productId, type, quantity, Ct))
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }
}
