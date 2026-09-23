using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StokTakip.IntegrationTests.Api.Products;
using Xunit;

namespace StokTakip.IntegrationTests.Api;

/// <summary>
/// D10: the product pattern, now for categories and suppliers. Two admins editing the same row
/// used to be decided by whoever saved last — the first edit vanished without either of them
/// knowing. The second save now has to be refused, and the row must keep the first edit.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogConcurrencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public CatalogConcurrencyTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await TestScratch.CleanupAsync(_db, CancellationToken.None);

    [Fact]
    public async Task Kategori_bayat_rowVersion_ile_PUT_409_ve_ilk_duzenleme_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var id = await TestScratch.CreateCategoryAsync(admin, "D10 Kategori", Ct);
        var opened = await GetAsync(admin, $"/api/categories/{id}");

        var first = await admin.PutAsJsonAsync($"/api/categories/{id}",
            new { name = TestScratch.NamePrefix + "D10 İlk", isActive = true, rowVersion = opened.RowVersion }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await admin.PutAsJsonAsync($"/api/categories/{id}",
            new { name = TestScratch.NamePrefix + "D10 İkinci", isActive = true, rowVersion = opened.RowVersion }, Ct);

        await AssertConcurrencyConflictAsync(second);
        Assert.Equal(TestScratch.NamePrefix + "D10 İlk", (await GetAsync(admin, $"/api/categories/{id}")).Name);
    }

    [Fact]
    public async Task Tedarikci_bayat_rowVersion_ile_PUT_409_ve_ilk_duzenleme_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var id = await TestScratch.CreateSupplierAsync(admin, "D10 Tedarikçi", Ct);
        var opened = await GetAsync(admin, $"/api/suppliers/{id}");

        var first = await admin.PutAsJsonAsync($"/api/suppliers/{id}",
            new
            {
                name = TestScratch.NamePrefix + "D10 İlk", contactEmail = "t4@stok.local",
                isActive = true, rowVersion = opened.RowVersion
            }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await admin.PutAsJsonAsync($"/api/suppliers/{id}",
            new
            {
                name = TestScratch.NamePrefix + "D10 İkinci", contactEmail = "t4@stok.local",
                isActive = true, rowVersion = opened.RowVersion
            }, Ct);

        await AssertConcurrencyConflictAsync(second);
        Assert.Equal(TestScratch.NamePrefix + "D10 İlk", (await GetAsync(admin, $"/api/suppliers/{id}")).Name);
    }

    private static async Task AssertConcurrencyConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // A 409 alone would also be a duplicate name; the frontend tells them apart by this code.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("concurrency_conflict", document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Row> GetAsync(HttpClient admin, string url) =>
        (await admin.GetFromJsonAsync<Row>(url, Ct))!;

    private sealed record Row(int Id, string Name, uint RowVersion);
}
