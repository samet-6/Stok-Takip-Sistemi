using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// Who may write a movement, whose name ends up on it, and whose movements each role may read.
/// The last one is the leak: the controller overwrites the caller's userId filter for a Çalışan,
/// and if that line ever goes the endpoint keeps working — it just starts answering questions
/// nobody was allowed to ask.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementIdentityTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementIdentityTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>Stock movement is operational data: every signed-in user may add it, not just Admin.
    /// This is the configuration-vs-operation split the project's rule 5 draws.</summary>
    [Fact]
    public async Task Calisan_hareket_ekleyebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDENT-01", initialStock: 5);

        var response = await MovementScratch.PostMovementAsync(calisan, product.Id, "In", 2, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(7, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>The control for the test above: the same token that may write a movement must still
    /// be refused on catalog writes. Without it, "Çalışan can post" would also pass on an API that
    /// had simply stopped checking roles.</summary>
    [Fact]
    public async Task Calisan_katalog_yazamiyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var response = await calisan.PostAsJsonAsync(
            "/api/categories", new { name = MovementScratch.NamePrefix + "Yasak Kategori" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Identity comes from the token, never from the body. The request deliberately carries both
    /// spellings a client might guess; neither exists on the request DTO, so both are ignored and
    /// the row is stamped with the caller.
    /// </summary>
    [Fact]
    public async Task createdByUserId_tokendan_geliyor_govdedeki_kullanici_yok_sayiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDENT-02", initialStock: 5);

        var adminId = await AdminIdAsync(admin, product.Id);
        var calisanId = await MovementScratch.SeedCalisanIdAsync(admin, Ct);

        var response = await calisan.PostAsJsonAsync(
            "/api/stock-movements",
            new
            {
                productId = product.Id,
                type = "In",
                quantity = 1,
                note = MovementScratch.NamePrefix + "kimlik",
                createdByUserId = adminId,
                userId = adminId
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<MovementScratch.MovementResult>(Ct))!;
        Assert.Equal(calisanId, result.Movement.CreatedByUserId);
        Assert.NotEqual(adminId, result.Movement.CreatedByUserId);
    }

    /// <summary>The name is resolved through IUserLookupService, not stored on the movement — so a
    /// broken lookup shows up as an empty string rather than an error.</summary>
    [Fact]
    public async Task createdByFullName_dogru_isimle_doluyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDENT-03", initialStock: 5);

        var result = await MovementScratch.AddMovementAsync(calisan, product.Id, "In", 1, Ct);

        Assert.Equal("Örnek Çalışan", result.Movement.CreatedByFullName);
    }

    /// <summary>
    /// The leak lock. A Çalışan asking for somebody else's movements is not refused — the filter is
    /// silently replaced with their own id, so the request succeeds and returns nothing it should
    /// not. Asserting "the admin's movement is absent" is the half that matters: an empty list
    /// would also satisfy a naive count check.
    /// </summary>
    [Fact]
    public async Task Calisan_baskasinin_userId_sini_gonderse_bile_yalniz_kendi_hareketlerini_goruyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDENT-04", initialStock: 20);

        var adminMovement = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 1, Ct);
        var calisanMovement = await MovementScratch.AddMovementAsync(calisan, product.Id, "In", 1, Ct);
        var adminId = adminMovement.Movement.CreatedByUserId;

        var page = await calisan.GetFromJsonAsync<MovementScratch.MovementPage>(
            $"/api/stock-movements?productId={product.Id}&userId={adminId}&pageSize=100", Ct);

        Assert.DoesNotContain(page!.Items, m => m.Id == adminMovement.Movement.Id);
        Assert.Contains(page.Items, m => m.Id == calisanMovement.Movement.Id);
        Assert.All(page.Items, m => Assert.NotEqual(adminId, m.CreatedByUserId));
    }

    /// <summary>The other half of the same switch: for an Admin the userId filter is honoured, so
    /// the two branches are proven separately rather than by one passing and one assumed.</summary>
    [Fact]
    public async Task Admin_userId_ile_bir_calisanin_hareketlerini_gorebiliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var product = await CreateProductAsync(admin, "IDENT-05", initialStock: 20);

        var adminMovement = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 1, Ct);
        var calisanMovement = await MovementScratch.AddMovementAsync(calisan, product.Id, "In", 1, Ct);
        var calisanId = calisanMovement.Movement.CreatedByUserId;

        var page = await admin.GetFromJsonAsync<MovementScratch.MovementPage>(
            $"/api/stock-movements?productId={product.Id}&userId={calisanId}&pageSize=100", Ct);

        Assert.Contains(page!.Items, m => m.Id == calisanMovement.Movement.Id);
        Assert.DoesNotContain(page.Items, m => m.Id == adminMovement.Movement.Id);
        Assert.All(page.Items, m => Assert.Equal(calisanId, m.CreatedByUserId));
    }

    /// <summary>The admin never appears in GET /api/users (that endpoint lists Çalışans only), so
    /// its id is taken from a movement the admin itself wrote.</summary>
    private static async Task<string> AdminIdAsync(HttpClient admin, int productId)
    {
        var result = await MovementScratch.AddMovementAsync(admin, productId, "In", 1, Ct);
        return result.Movement.CreatedByUserId;
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }
}
