using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Realtime;

/// <summary>
/// When the application signals, and — more importantly — when it does not.
/// <para>
/// The rule under test is one sentence: <b>the signal follows the row, not the request.</b> An
/// edit that was refused changed nothing, and a PUT that re-sends identical values writes nothing;
/// broadcasting either would have every open screen refetch data it already has. None of these
/// have a user-visible symptom when they regress — the application just becomes chattier — so a
/// test is the only thing standing between the rule and quiet decay.
/// </para>
/// <para>
/// Asserted over a real hub connection rather than against a stubbed notifier. A negative checked
/// at the notifier boundary only proves the method was not called; over a socket it proves nothing
/// reached the client, which is the promise that actually matters. The two devices that make a
/// negative meaningful here are a <b>control signal</b> — a change that provably travels the same
/// socket afterwards — and, for the ordering rule, a held row lock.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SignalDisciplineTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public SignalDisciplineTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// Karar 6 — the signal is raised after the transaction commits, never before. A listener that
    /// refetches on an uncommitted signal reads the old row and ends up staler than if nothing had
    /// been sent at all: the one outcome worse than silence.
    /// <para>
    /// Ordering is forced rather than observed. An open transaction takes the product row with
    /// <c>FOR UPDATE</c> — a pure lock, no write, so the concurrency token never moves and the
    /// movement is not sent down the retry path. The movement's own UPDATE then blocks, and for as
    /// long as it is blocked <b>nothing may have been announced</b>. That mid-flight assertion is
    /// the whole test; signalling before <c>SaveChanges</c> would put a message on the socket while
    /// the write was still pending.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Sinyal_commit_ten_sonra_atiliyor_bekleyen_yazi_sirasinda_sessiz_kaliyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "SIG-01", initialStock: 10);

        await using var client = await ConnectAsync(admin);

        await using var blocker = _db.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(Ct);
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Products\" WHERE \"Id\" = {product.Id} FOR UPDATE", Ct);

        var pending = MovementScratch.PostMovementAsync(admin, product.Id, "In", 7, Ct);
        await Task.Delay(600, Ct);

        Assert.DoesNotContain(client.Received, s => s.Target == RealtimeEvents.ProductChanged);

        await transaction.CommitAsync(Ct);
        Assert.Equal(HttpStatusCode.Created, (await pending).StatusCode);

        var signal = await client.WaitForAsync(RealtimeEvents.ProductChanged, Ct);
        Assert.Equal(product.Id.ToString(), Assert.Single(signal.Arguments));

        // And what a client refetching on that signal reads is the committed value.
        var refetched = await admin.GetFromJsonAsync<MovementScratch.Product>(
            $"/api/products/{product.Id}", Ct);
        Assert.Equal(17, refetched!.StockQuantity);
    }

    /// <summary>A refused edit changed no row, so there is nothing to tell anyone about.</summary>
    [Fact]
    public async Task Catisma_alan_duzenleme_sinyal_atmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "SIG-02", initialStock: 5);

        var first = await PutAsync(admin, product, "T5 Sinyal Düzenleme", product.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Connected only now: creating and editing the product both signal legitimately, and the
        // socket should start empty so the assertion below cannot trip over the setup.
        await using var client = await ConnectAsync(admin);

        var conflicted = await PutAsync(admin, product, "T5 Bayat Düzenleme", product.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, conflicted.StatusCode);

        await AssertSilentAboutAsync(client, admin, product.Id);
    }

    /// <summary>
    /// A movement refused for insufficient stock leaves the ledger and the quantity untouched, so
    /// no product signal. It does write a rejection notice, and that one <i>is</i> announced — so
    /// this test carries its control inside the very same request: the notification signal proves
    /// the request reached the notifier, which is what makes the missing product signal a decision
    /// rather than an accident.
    /// </summary>
    [Fact]
    public async Task Yetersiz_stok_400_u_urun_sinyali_atmiyor_ama_bildirim_sinyali_atiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "SIG-03", initialStock: 3);

        await using var client = await ConnectAsync(admin);

        var response = await MovementScratch.PostMovementAsync(admin, product.Id, "Out", 5, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await client.WaitForAsync(RealtimeEvents.NotificationsChanged, Ct);
        await client.SettleAsync(Ct);

        Assert.DoesNotContain(client.Received, s => s.Target == RealtimeEvents.ProductChanged);
    }

    /// <summary>
    /// A PUT that re-sends identical values makes EF's change tracker emit no UPDATE at all, and
    /// <c>SaveChangesAsync</c> returns 0. The guard on that return value is the most quietly
    /// regression-prone line in the service: remove it and nothing breaks, nothing errors, every
    /// connected screen just refetches data it already has on every no-op save.
    /// </summary>
    [Fact]
    public async Task Hicbir_alani_degistirmeyen_PUT_sinyal_atmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "SIG-04", initialStock: 5);

        await using var client = await ConnectAsync(admin);

        var response = await PutAsync(admin, product, product.Name, product.RowVersion);

        // 204, not 409: nothing changed, so the version in hand is still current.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertSilentAboutAsync(client, admin, product.Id);
    }

    /// <summary>
    /// The second branch of the same guard. A product with movements is soft-deleted rather than
    /// removed, and soft-deleting one that is already inactive writes nothing — so it signals
    /// nothing, even though the endpoint still answers 200.
    /// </summary>
    [Fact]
    public async Task Zaten_pasif_bir_urunu_silmek_sinyal_atmiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);

        // The opening movement is what puts DELETE on the soft path instead of removing the row.
        var product = await CreateProductAsync(admin, "SIG-05", initialStock: 5);
        var deactivate = await PutAsync(admin, product, product.Name, product.RowVersion, isActive: false);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        await using var client = await ConnectAsync(admin);

        var deleted = await admin.DeleteAsync($"/api/products/{product.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        await AssertSilentAboutAsync(client, admin, product.Id);
    }

    /// <summary>
    /// Asserts nothing was announced about a product, and does it against a control rather than a
    /// clock. A fresh product is created — an operation that provably does signal — and its arrival
    /// is awaited on the same connection. Only then is the absence checked: a signal suppressed by
    /// the rule under test would have been dispatched before the control was even requested, so if
    /// the control got through and it did not, it was never sent.
    /// <para>
    /// The settle on top covers the one gap in that argument: signals are dispatched fire-and-forget,
    /// so two of them from two different requests are ordered by when they were raised, not
    /// guaranteed by the transport.
    /// </para>
    /// </summary>
    private async Task AssertSilentAboutAsync(TestHubClient client, HttpClient admin, int productId)
    {
        var control = await CreateProductAsync(admin, $"CTRL-{productId}", initialStock: 1);

        await client.WaitForAsync(RealtimeEvents.ProductChanged, Ct);
        await client.SettleAsync(Ct);

        Assert.Contains(
            client.Received,
            s => s.Target == RealtimeEvents.ProductChanged && s.Arguments[0] == control.Id.ToString());

        Assert.DoesNotContain(
            client.Received,
            s => s.Target == RealtimeEvents.ProductChanged && s.Arguments[0] == productId.ToString());
    }

    private async Task<TestHubClient> ConnectAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/auth/hub-ticket", null, Ct);
        response.EnsureSuccessStatusCode();

        var ticket = (await response.Content.ReadFromJsonAsync<Ticket>(Ct))!.Token;

        return await TestHubClient.ConnectAsync(_db.Factory, ticket, Ct);
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock);
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin, MovementScratch.Product product, string name, uint rowVersion,
        bool? isActive = null)
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
                isActive = isActive ?? product.IsActive,
                rowVersion
            },
            Ct);

    private sealed record Ticket(string Token);
}
