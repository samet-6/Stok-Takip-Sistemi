using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Movements;

/// <summary>
/// What the endpoint promises beyond any single request: the ledger stays the source of truth for
/// StockQuantity, a re-sent movement is a second movement, and errors come back in one shape
/// clients can branch on.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MovementContractTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public MovementContractTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// Fifty movements in a shuffled order across three products, then the invariant that gives the
    /// whole append-only design its point: StockQuantity is not an independent number, it is the
    /// ledger's sum. The starting stock is large enough that no draw can outrun it (fifty moves of
    /// at most five, against three hundred), so every request must be accepted — a test that
    /// tolerated rejections could pass while quietly doing almost nothing.
    /// </summary>
    [Fact]
    public async Task Rastgele_yuk_sonrasi_her_urunun_stogu_hareket_netine_esit()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        var products = new List<MovementScratch.Product>();
        for (var i = 1; i <= 3; i++)
            products.Add(await CreateProductAsync(admin, $"LOAD-0{i}", initialStock: 300));

        // Fixed seed: a failure has to be reproducible, and "it passed yesterday" is not a result.
        var random = new Random(20260729);

        for (var i = 0; i < 50; i++)
        {
            var product = products[random.Next(products.Count)];
            var type = random.Next(2) == 0 ? "In" : "Out";
            var quantity = random.Next(1, 6);

            var response = await MovementScratch.PostMovementAsync(admin, product.Id, type, quantity, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        foreach (var product in products)
            Assert.Equal(
                await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
                await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Documented behaviour, not an oversight: there is no idempotency key, so an identical second
    /// request is a second real movement. A client that cannot tell whether its first attempt
    /// committed is asked to check and re-send rather than retry blindly — that decision only makes
    /// sense while this test holds, which is why it is pinned rather than left implicit.
    /// </summary>
    [Fact]
    public async Task Ayni_hareket_iki_kez_gonderilince_iki_kayit_olusuyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONTRACT-01", initialStock: 10);
        var rowsBefore = await MovementScratch.MovementCountAsync(_db, product.Id, Ct);

        var first = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 4, Ct, "T5 aynı hareket");
        var second = await MovementScratch.AddMovementAsync(admin, product.Id, "In", 4, Ct, "T5 aynı hareket");

        Assert.NotEqual(first.Movement.Id, second.Movement.Id);
        Assert.Equal(rowsBefore + 2, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(18, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// Errors routed through GlobalExceptionHandler carry a stable machine-readable `code`, because
    /// that is where one status maps to several distinguishable causes — 409 is both "this name is
    /// taken" and "somebody changed the row while you were editing", and the frontend branches on
    /// exactly that difference. Matching the Turkish title instead would put control flow at the
    /// mercy of a wording edit.
    /// </summary>
    [Fact]
    public async Task Handler_yolundan_gecen_hatalar_beklenen_code_degerini_tasiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONTRACT-02", initialStock: 5);

        var badRequest = await MovementScratch.PostMovementAsync(admin, int.MaxValue, "In", 1, Ct);
        await AssertProblemAsync(badRequest, HttpStatusCode.BadRequest, "bad_request");

        var notFound = await admin.GetAsync($"/api/products/{int.MaxValue}", Ct);
        await AssertProblemAsync(notFound, HttpStatusCode.NotFound, "not_found");

        // A category name the seed already holds — the duplicate is refused before a row is written.
        var conflict = await admin.PostAsJsonAsync("/api/categories", new { name = "Elektronik" }, Ct);
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "conflict");

        // The first edit has to change something. A PUT whose values all match the stored row saves
        // nothing, so xmin never moves and the "stale" version stays perfectly current — the second
        // call would then come back 204 and this test would be asserting nothing at all.
        var first = await PutProductAsync(admin, product, product.RowVersion, "T5 Sözleşme Düzenleme");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var conflicted = await PutProductAsync(admin, product, product.RowVersion, "T5 Bayat Düzenleme");
        await AssertProblemAsync(conflicted, HttpStatusCode.Conflict, "concurrency_conflict");
    }

    /// <summary>
    /// The other half of the same contract, and a deliberate asymmetry rather than a gap left open.
    /// JWT 401/403 and framework model-validation 400 never reach the handler and carry no `code` —
    /// they do not need one: 401 and 403 are fully described by their status, and a validation 400
    /// is discriminated by its `errors` dictionary. `code` earns its place only where one status has
    /// several causes. What all three must still honour is the envelope: RFC 7807 problem+json.
    ///
    /// If a new error path is ever added, this is the rule to apply.
    /// </summary>
    [Fact]
    public async Task JWT_401_403_ve_model_400_problem_json_donuyor_ama_code_tasimiyor()
    {
        using var anonymous = _db.Factory.CreateClient();
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "CONTRACT-03", initialStock: 5);

        var unauthorized = await MovementScratch.PostMovementAsync(anonymous, product.Id, "In", 1, Ct);
        await AssertProblemAsync(unauthorized, HttpStatusCode.Unauthorized, expectedCode: null);

        var forbidden = await calisan.PostAsJsonAsync(
            "/api/categories", new { name = MovementScratch.NamePrefix + "Yasak" }, Ct);
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, expectedCode: null);

        // Quantity has [Range(1, …)], so this never reaches the service.
        var invalid = await MovementScratch.PostMovementAsync(admin, product.Id, "In", 0, Ct);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, expectedCode: null);

        using var document = System.Text.Json.JsonDocument.Parse(
            await invalid.Content.ReadAsStringAsync(Ct));
        Assert.True(
            document.RootElement.TryGetProperty("errors", out var errors),
            "Model doğrulama 400'ü alan hatalarını taşımalı — code yerine ayırt edici olan bu.");
        Assert.True(errors.TryGetProperty("Quantity", out _));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string? expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, await MovementScratch.CodeAsync(response, Ct));
    }

    private static Task<HttpResponseMessage> PutProductAsync(
        HttpClient admin, MovementScratch.Product product, uint rowVersion, string name)
        => admin.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            new
            {
                name,
                sku = product.SKU,
                categoryId = product.CategoryId,
                supplierId = product.SupplierId,
                unitPrice = product.UnitPrice,
                minStockLevel = product.MinStockLevel,
                isActive = product.IsActive,
                rowVersion
            },
            Ct);

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }
}
