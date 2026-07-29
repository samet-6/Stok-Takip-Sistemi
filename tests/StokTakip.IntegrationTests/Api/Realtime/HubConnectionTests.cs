using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.Api.Realtime;
using StokTakip.Application.Common;
using StokTakip.IntegrationTests.Api.Movements;
using Xunit;

namespace StokTakip.IntegrationTests.Api.Realtime;

/// <summary>
/// What actually reaches a live connection. Everything here needs a real socket: the audience of a
/// signal is decided at connect time (group membership from the ticket's role) and there is no way
/// to observe it from the outside — a signal sent to nobody looks exactly like a signal sent
/// successfully, which is the failure mode these tests exist for.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HubConnectionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _db;

    public HubConnectionTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Shares the T5 sweep: these tests create products through the same helpers, so the
    /// rows carry the same prefix and are cleared the same way.</summary>
    public async ValueTask DisposeAsync() => await MovementScratch.CleanupAsync(_db, CancellationToken.None);

    /// <summary>
    /// End to end: a business action commits, the notifier fires, a connected client hears about it.
    /// The payload is asserted too — the id has to be the product that actually changed, and it has
    /// to be the only thing sent. Karar 1 says the signal is an invalidation key, not data.
    /// </summary>
    [Fact]
    public async Task IRealtimeNotifier_cagrisi_bagli_istemciye_ulasiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        var product = await CreateProductAsync(admin, "HUB-01", initialStock: 10, minStockLevel: 0);

        await using var client = await ConnectAsync(admin);

        await MovementScratch.AddMovementAsync(admin, product.Id, "In", 3, Ct);

        var signal = await client.WaitForAsync(RealtimeEvents.ProductChanged, Ct);

        var id = Assert.Single(signal.Arguments);
        Assert.Equal(product.Id.ToString(), id);
    }

    /// <summary>
    /// The group fence. A Çalışan cannot read notifications, so telling them the list changed would
    /// leak a fact they have no endpoint to act on.
    /// <para>
    /// Three assertions in one test on purpose, because each one is worthless alone: the admin
    /// receiving <c>NotificationsChanged</c> proves the signal was emitted at all, the Çalışan
    /// receiving <c>ProductChanged</c> proves their socket is alive and listening, and only against
    /// those two does the absent <c>NotificationsChanged</c> mean anything. Drop either control and
    /// this test passes on a hub that sends nothing to anyone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NotificationsChanged_yalniz_admine_ulasiyor_ProductChanged_ikisine_de()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        // Threshold at 5, stock 10: an Out of 6 lands below it and writes a LowStock notification.
        var product = await CreateProductAsync(admin, "HUB-02", initialStock: 10, minStockLevel: 5);

        await using var adminClient = await ConnectAsync(admin);
        await using var calisanClient = await ConnectAsync(calisan);

        await MovementScratch.AddMovementAsync(admin, product.Id, "Out", 6, Ct);

        await adminClient.WaitForAsync(RealtimeEvents.NotificationsChanged, Ct);
        await calisanClient.WaitForAsync(RealtimeEvents.ProductChanged, Ct);
        await calisanClient.SettleAsync(Ct);

        Assert.DoesNotContain(
            calisanClient.Received,
            s => s.Target == RealtimeEvents.NotificationsChanged);
    }

    /// <summary>
    /// <c>HubUserIdProvider</c> resolves a connection's identity from the raw <c>sub</c> claim,
    /// because SignalR's default reads <c>ClaimTypes.NameIdentifier</c> and this API sets
    /// <c>MapInboundClaims = false</c> — nothing ever populates that claim.
    /// <para>
    /// Driven through <c>IHubContext</c> rather than a business action, because no production code
    /// calls <c>Clients.User</c> today: real-time session revocation (S2) was the user-targeted
    /// signal and it was cancelled, leaving the provider's only live consumer the
    /// <c>Context.UserIdentifier</c> in the hub's log lines (covered below). So this test guards a
    /// capability that is registered and wired but not yet used — worth pinning precisely because
    /// its failure mode is silence: <c>Clients.User(id)</c> compiles, runs, reports no error, and
    /// reaches nobody.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Clients_User_sub_claimiyle_hedefe_ulasiyor()
    {
        using var admin = await _db.Factory.AsAdminAsync(Ct);
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);

        var adminId = await UserIdAsync(StokTakipFactory.AdminEmail);

        await using var adminClient = await ConnectAsync(admin);
        await using var calisanClient = await ConnectAsync(calisan);

        var hub = _db.Factory.Services.GetRequiredService<IHubContext<StokHub>>();
        await hub.Clients.User(adminId).SendAsync(RealtimeEvents.ProductChanged, 4242, Ct);

        var signal = await adminClient.WaitForAsync(RealtimeEvents.ProductChanged, Ct);
        Assert.Equal("4242", Assert.Single(signal.Arguments));

        // Addressed to one user, so the other connection must stay silent — otherwise the provider
        // could be returning a constant and this would still look like a delivery.
        await calisanClient.SettleAsync(Ct);
        Assert.Empty(calisanClient.Received);
    }

    /// <summary>
    /// The hub logs the resolved identifier on both edges of a connection. This is also the only
    /// production consumer of <c>HubUserIdProvider</c> today, so it is what would actually break if
    /// the provider were removed — a null user in every hub log line, and no other symptom.
    /// </summary>
    [Fact]
    public async Task Hub_connect_ve_disconnect_loglari_dogru_sub_u_yaziyor()
    {
        using var calisan = await _db.Factory.AsCalisanAsync(Ct);
        var userId = await UserIdAsync(StokTakipFactory.UserEmail);

        var logs = CapturedLogs.Attach(_db.Factory.Services, typeof(StokHub));

        var client = await ConnectAsync(calisan);
        await logs.WaitForAsync(
            line => line.StartsWith("Hub connected:") && line.Contains(userId), Ct);

        await client.DisposeAsync();
        await logs.WaitForAsync(
            line => line.StartsWith("Hub disconnected:") && line.Contains(userId), Ct);
    }

    private async Task<TestHubClient> ConnectAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/auth/hub-ticket", null, Ct);
        response.EnsureSuccessStatusCode();

        var ticket = (await response.Content.ReadFromJsonAsync<Ticket>(Ct))!.Token;

        return await TestHubClient.ConnectAsync(_db.Factory, ticket, Ct);
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var context = _db.CreateContext();

        return await context.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct);
    }

    private async Task<MovementScratch.Product> CreateProductAsync(
        HttpClient admin, string sku, int initialStock, int minStockLevel)
    {
        var (categoryId, supplierId) = await MovementScratch.SeedCatalogAsync(_db, Ct);

        return await MovementScratch.CreateProductAsync(
            admin, sku, categoryId, supplierId, Ct, initialStock, minStockLevel);
    }

    private sealed record Ticket(string Token);
}
