using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// The ledger's core promises: a movement moves the stock by exactly its quantity, and a refused
/// movement moves nothing at all. The second half is the one worth testing — a rejection that
/// still wrote half of itself would leave StockQuantity and the ledger disagreeing forever, and
/// nothing downstream would notice.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementBasicsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementBasicsTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task In_hareketi_201_donuyor_ve_stok_dogru_artiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "BASIC-01", initialStock: 10);

        var response = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 5, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<MovementScratch.MovementResult>(Ct))!;
        Assert.Equal(15, result.NewStockQuantity);
        Assert.Equal("In", result.Movement.Type);
        Assert.Equal(5, result.Movement.Quantity);

        // The response's number and the row's number are two different claims; the endpoint could
        // report a total it never wrote.
        Assert.Equal(15, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    [Fact]
    public async Task Stok_dahilindeki_Out_hareketi_201_donuyor_ve_stok_dogru_azaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "BASIC-02", initialStock: 10);

        var response = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 4, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<MovementScratch.MovementResult>(Ct))!;
        Assert.Equal(6, result.NewStockQuantity);
        Assert.Equal(6, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Atomicity, stated as three separate assertions because a partial write satisfies any one of
    /// them alone: the caller is told no, the stock is untouched, and no row was appended. The
    /// movement insert and the quantity update share one SaveChanges precisely so this holds.
    /// </summary>
    [Fact]
    public async Task Stogu_asan_Out_400_donuyor_ve_ne_stok_ne_hareket_satiri_degisiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "BASIC-03", initialStock: 3);

        var movementsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var response = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 5, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The number in the message is what makes it actionable — it tells the user what is
        // actually on hand, so a generic "yetersiz stok" would be a regression.
        Assert.Equal("Yetersiz stok. Mevcut: 3", await MovementScratch.TitleAsync(response, Ct));

        Assert.Equal(3, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(movementsBefore, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Both directions, deliberately. Allowing Out so leftover stock could be drained was rejected
    /// (Sam, 2026-07-27): "can I move this product?" must not have a direction-dependent answer.
    /// Testing only one direction would let the other half be relaxed without anything turning red.
    /// </summary>
    [Fact]
    public async Task Pasif_urune_hareket_her_iki_yonde_de_400_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "BASIC-04", initialStock: 10);
        await MovementScratch.DeactivateProductAsync(admin, product, Ct);

        var movementsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);
        const string expected = "Ürün pasif; stok hareketi için önce ürünü aktifleştirin.";

        var incoming = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 1, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, incoming.StatusCode);
        Assert.Equal(expected, await MovementScratch.TitleAsync(incoming, Ct));

        var outgoing = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 1, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, outgoing.StatusCode);
        Assert.Equal(expected, await MovementScratch.TitleAsync(outgoing, Ct));

        Assert.Equal(10, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(movementsBefore, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
    }

    /// <summary>400, not 404: the id arrives in the request body as a value that failed validation,
    /// not as the address of the resource being asked for.</summary>
    [Fact]
    public async Task Olmayan_productId_400_donuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var response = await MovementScratch.PostMovementAsync(admin, int.MaxValue, "In", 1, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ürün bulunamadı", await MovementScratch.TitleAsync(response, Ct));
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }
}
