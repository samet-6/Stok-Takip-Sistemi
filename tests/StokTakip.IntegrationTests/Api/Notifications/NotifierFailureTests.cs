using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StokTakip.Application.Common;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// What happens to a stock movement when the signalling layer misbehaves.
/// <para>
/// This is the one place in the suite where a stand-in notifier is the subject rather than a
/// shortcut: a notifier that fails cannot be produced with the real one, which catches everything
/// inside its own dispatch. Everywhere else — see the signal-discipline tests — the assertions run
/// against a live hub connection precisely because a stub would only prove a method was called.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotifierFailureTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;

    public NotifierFailureTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = _db.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRealtimeNotifier>();
                services.AddSingleton<IRealtimeNotifier, ThrowingNotifier>();
            }));

        _admin = _factory.CreateClient();
        var token = await AuthenticatedClient.LoginAsync(
            _admin, StokTakipFactory.AdminEmail, StokTakipFactory.AdminPassword, Ct);
        _admin.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        await MovementScratch.CleanupAsync(_db, CancellationToken.None);
    }

    /// <summary>
    /// The ledger is not hostage to the notification layer: a movement that committed stays
    /// committed even if nobody can be told about it. The transaction is already closed by the time
    /// the signal is raised, so a failure there cannot reach back into it.
    /// <para>
    /// Worth being precise about what this does <b>not</b> promise. The request does not return
    /// 201 — the exception escapes after the commit, so the caller sees 500 for an operation that
    /// succeeded. That gap is harmless in production only because the real notifier swallows its
    /// own failures; the guarantee tested here is about the database, not about the status code.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bildirim_yayini_hata_verse_de_stok_hareketi_kaydediliyor()
    {
        // Set up through the ordinary host: creating a product signals too, and this host's
        // notifier throws on every signal — the arrangement would fail before the subject ran.
        // Same database either way, so the movement below acts on the same row.
        using var setup = await _db.Factory.AsAdminAsync(Ct);
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);
        var product = await MovementScratch.CreateProductAsync(
            setup, "FAIL-01", categoryId, supplierId, Ct, initialStock: null);

        var response = await MovementScratch.PostMovementAsync(_admin, product.Id, "In", 4, Ct);

        // The signal escapes after the commit, so the caller is told the request failed.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal(4, await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
        Assert.Equal(1, await MovementScratch.MovementCountAsync(_db, product.Id, Ct));
        Assert.Equal(
            await MovementScratch.LedgerNetAsync(_db, product.Id, Ct),
            await MovementScratch.StockQuantityAsync(_db, product.Id, Ct));
    }

    /// <summary>
    /// The "signalling never delays a business transaction" rule, tested where it actually lives:
    /// in the shape of the contract. Every <see cref="IRealtimeNotifier"/> method returns
    /// <c>void</c>, so a caller physically cannot await a signal and hold a transaction open behind
    /// a hub dispatch — change one to <c>Task</c> and the next person to touch a service will
    /// await it, and nothing else in the suite would notice.
    /// <para>
    /// The other half of the guarantee — that <c>SignalRNotifier</c> discards the send task rather
    /// than awaiting it — is a code shape, not a runtime-observable fact: any stand-in built to
    /// measure it would only be measuring itself. It stays covered by review, and is called out
    /// here so the gap is deliberate rather than assumed.
    /// </para>
    /// </summary>
    [Fact]
    public void Sinyal_arayuzu_beklenebilir_degil()
    {
        var methods = typeof(IRealtimeNotifier).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.Equal(typeof(void), m.ReturnType));
    }

    /// <summary>Fails on every signal, the way the real notifier never does.</summary>
    private sealed class ThrowingNotifier : IRealtimeNotifier
    {
        public void NotifyProductChanged(int productId)
            => throw new InvalidOperationException("B0: sinyal katmani coktu.");

        public void NotifyNotificationsChanged()
            => throw new InvalidOperationException("B0: sinyal katmani coktu.");
    }
}
