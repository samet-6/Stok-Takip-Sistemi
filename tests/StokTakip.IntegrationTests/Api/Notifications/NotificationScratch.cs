using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using StokTakip.IntegrationTests.Api.Movements;

namespace StokTakip.IntegrationTests.Api.Notifications;

/// <summary>
/// Helpers for the notification tests. Products are created through the T5 helpers and carry the
/// same prefix, so <see cref="MovementScratch.CleanupAsync"/> sweeps the notifications too — T1
/// asserts the seeded database holds none, and every row written here hangs off a swept product.
/// </summary>
internal static class NotificationScratch
{
    /// <summary>Notifications for one product, oldest first. Read from the database rather than
    /// the endpoint: most of these tests are about what was written, not what is shown.</summary>
    public static async Task<List<Notification>> ForProductAsync(
        TestDatabaseFixture db, int productId, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        return await context.Notifications
            .AsNoTracking()
            .Where(n => n.ProductId == productId)
            .OrderBy(n => n.Id)
            .ToListAsync(ct);
    }

    public static async Task<int> CountAsync(
        TestDatabaseFixture db, int productId, NotificationType type, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        return await context.Notifications.CountAsync(n => n.ProductId == productId && n.Type == type, ct);
    }

    /// <summary>Total across every product — the endpoint's counters are global, so tests that
    /// assert on them need to know the table is otherwise empty.</summary>
    public static async Task<int> TotalCountAsync(TestDatabaseFixture db, CancellationToken ct)
    {
        await using var context = db.CreateContext();

        return await context.Notifications.CountAsync(ct);
    }

    public static async Task<NotificationPage> GetPageAsync(
        HttpClient admin, string query, CancellationToken ct)
        => (await admin.GetFromJsonAsync<NotificationPage>($"/api/notifications?{query}", ct))!;

    /// <summary>
    /// Drives a product below its minimum by taking stock out, and returns the movement response.
    /// Threshold crossings are always produced through the real endpoint — writing a notification
    /// row directly would test nothing, since detection is the whole subject.
    /// </summary>
    public static Task<HttpResponseMessage> TakeOutAsync(
        HttpClient client, int productId, int quantity, CancellationToken ct)
        => MovementScratch.PostMovementAsync(client, productId, "Out", quantity, ct);

    public static Task<HttpResponseMessage> PutInAsync(
        HttpClient client, int productId, int quantity, CancellationToken ct)
        => MovementScratch.PostMovementAsync(client, productId, "In", quantity, ct);

    /// <summary>Local shape, so a renamed JSON field breaks the test instead of moving on both
    /// sides at once. The enum arrives as a string — that is the wire format.</summary>
    internal sealed record NotificationRow(
        int Id,
        string Type,
        int ProductId,
        string ProductName,
        int Quantity,
        int? RequestedQuantity,
        DateTime CreatedAt,
        string CreatedByUserId,
        string CreatedByFullName,
        DateTime? ReadAt);

    internal sealed record NotificationPage(
        IReadOnlyList<NotificationRow> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages,
        int UnreadCount);
}
