using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// D27: a movement carries an Idempotency-Key, and one key is one movement however many times it
/// arrives. The scenario is ordinary — the save committed, the answer got lost on the way back,
/// the user presses Kaydet again — and without the key that second press was a second movement:
/// stock moved twice, and an append-only ledger cannot take the copy back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementIdempotencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementIdempotencyTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Ayni_anahtarla_ikinci_gonderim_yeni_kayit_acmiyor_ilk_hareketi_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-01", initialStock: 10);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);
        var key = Guid.NewGuid();

        var first = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 4, Ct, "T5 idem", key);
        var second = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 4, Ct, "T5 idem", key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstBody = await ReadAsync(first);
        var secondBody = await ReadAsync(second);
        Assert.Equal(firstBody.Movement.Id, secondBody.Movement.Id);

        Assert.Equal(rowsBefore + 1, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(14, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        // "newStockQuantity" means stock now, on a replay too.
        Assert.Equal(14, secondBody.NewStockQuantity);
    }

    /// <summary>
    /// The everyday case: the Out that emptied the shelf committed, its answer was lost, and the
    /// user sends it again. Judged afresh it would be "Yetersiz stok" — for a request that
    /// succeeded — so a known key is answered before any rule is applied.
    /// </summary>
    [Fact]
    public async Task Stogu_bitiren_cikis_ayni_anahtarla_tekrar_gelince_ilk_hareketi_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-08", initialStock: 3);
        var key = Guid.NewGuid();

        var first = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key);
        var again = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal((await ReadAsync(first)).Movement.Id, (await ReadAsync(again)).Movement.Id);
        Assert.Equal(0, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The same key with different content is not a retry, it is a client bug or a reused key —
    /// and silently answering with the first movement would tell the user their changed quantity
    /// was saved. Refused, and nothing is written.
    /// </summary>
    [Fact]
    public async Task Ayni_anahtar_farkli_icerikle_422_ve_hicbir_sey_yazilmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-02", initialStock: 10);
        var key = Guid.NewGuid();

        await MovementScratch.PostMovementAsync(admin, product.Id, "In", 4, Ct, "T5 idem", key);
        var rowsAfterFirst = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var changed = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 5, Ct, "T5 idem", key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed.StatusCode);
        Assert.Equal("idempotency_key_reused", await MovementScratch.CodeAsync(changed, Ct));
        Assert.Equal(rowsAfterFirst, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(14, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>A key belongs to whoever sent it first; another user cannot claim that movement.</summary>
    [Fact]
    public async Task Ayni_anahtar_baska_kullanicidan_gelince_422()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-03", initialStock: 10);
        var key = Guid.NewGuid();

        await MovementScratch.PostMovementAsync(admin, product.Id, "In", 4, Ct, "T5 idem", key);
        var other = await MovementScratch.PostMovementAsync(calisan, product.Id, "In", 4, Ct, "T5 idem", key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal("idempotency_key_reused", await MovementScratch.CodeAsync(other, Ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Gecerli_anahtar_yoksa_400_idempotency_key_required(string? header)
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-04", initialStock: 10);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stock-movements")
        {
            Content = JsonContent.Create(new { productId = product.Id, type = "In", quantity = 1 })
        };
        if (header is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", header);

        var response = await admin.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await MovementScratch.CodeAsync(response, Ct));
        Assert.Equal(rowsBefore, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Both copies in flight at once — neither can see the other's row when it checks, so only the
    /// unique index stands between them. One row, and both callers told about that same row.
    /// </summary>
    [Fact]
    public async Task Ayni_anahtarla_eszamanli_iki_gonderim_tek_kayit_uretiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-05", initialStock: 10);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(
            MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key),
            MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await ReadAsync(r)).Movement.Id));
        Assert.Equal(ids[0], ids[1]);

        Assert.Equal(rowsBefore + 1, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(7, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The twin that loses the race finds the product changed, and the server's own retry would
    /// judge it again — against stock its twin has just used up. Judged like that, a request that
    /// in fact succeeded comes back "Yetersiz stok" and even files a rejection notice. The key is
    /// checked before re-judging, so both copies get the one movement. Repeated because which copy
    /// loses, and how, is up to the scheduler.
    /// </summary>
    [Fact]
    public async Task Ayni_anahtarla_eszamanli_iki_cikis_tum_stogu_cekince_ikisi_de_ayni_hareketi_aliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        for (var round = 1; round <= 5; round++)
        {
            var product = await CreateProductAsync(admin, $"IDEM-07-{round}", initialStock: 3);
            var key = Guid.NewGuid();

            var responses = await Task.WhenAll(
                MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key),
                MovementScratch.PostMovementAsync(admin, product.Id, "Out", 3, Ct, "T5 idem", key));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Equal(0, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
            // The one real movement emptied the shelf: one OutOfStock, and no rejection notice.
            var notice = Assert.Single(await Notifications.NotificationScratch.ForProductAsync(_db, product.Id, Ct));
            Assert.Equal(Domain.Enums.NotificationType.OutOfStock, notice.Type);
        }
    }

    /// <summary>
    /// A refused Out writes no movement, so there is nothing to hold the key: re-sending it is
    /// judged again, against the stock as it is by then.
    /// </summary>
    [Fact]
    public async Task Reddedilen_cikis_anahtari_tuketmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "IDEM-06", initialStock: 2);
        var key = Guid.NewGuid();

        var refused = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 5, Ct, "T5 idem", key);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 10, Ct);

        var accepted = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 5, Ct, "T5 idem", key);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(7, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    private static async Task<MovementScratch.MovementResult> ReadAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<MovementScratch.MovementResult>(Ct))!;

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }
}
