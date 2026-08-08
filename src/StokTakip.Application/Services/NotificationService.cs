using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Application.Notifications;

namespace StokTakip.Application.Services;

/// <summary>
/// Read side of the notification feature. The write side lives where the events happen —
/// <see cref="StockMovementService"/> — because a notification row must share the transaction
/// that caused it; a separate writer here could only be called after that commit, which is
/// exactly the gap this design avoids.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IAppDbContext _db;
    private readonly IUserLookupService _userLookup;
    private readonly IRealtimeNotifier _realtime;

    public NotificationService(
        IAppDbContext db, IUserLookupService userLookup, IRealtimeNotifier realtime)
    {
        _db = db;
        _userLookup = userLookup;
        _realtime = realtime;
    }

    public async Task<NotificationListResponse> GetPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = _db.Notifications.AsNoTracking();

        var totalCount = await q.CountAsync(ct);
        var unreadCount = await q.CountAsync(n => n.ReadAt == null, ct);

        var rows = await q
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)   // same tiebreak as movements: deterministic paging
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                n.Id, n.Type, n.ProductId, ProductName = n.Product.Name,
                n.Quantity, n.RequestedQuantity, n.CreatedAt, n.CreatedByUserId, n.ReadAt
            })
            .ToListAsync(ct);

        var names = await _userLookup.GetFullNamesAsync(rows.Select(r => r.CreatedByUserId), ct);

        var items = rows
            .Select(r => new NotificationDto(
                r.Id, r.Type, r.ProductId, r.ProductName, r.Quantity, r.RequestedQuantity,
                r.CreatedAt, r.CreatedByUserId,
                names.TryGetValue(r.CreatedByUserId, out var fullName) ? fullName : string.Empty,
                r.ReadAt))
            .ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new NotificationListResponse(
            items, page, pageSize, totalCount, totalPages, unreadCount);
    }

    public async Task<bool> MarkReadAsync(int id, CancellationToken ct)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row is null) return false;

        // Already read is success, not a no-op error: two clicks must not produce two answers.
        if (row.ReadAt is null)
        {
            row.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _realtime.NotifyNotificationsChanged();
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var unread = await _db.Notifications.Where(n => n.ReadAt == null).ToListAsync(ct);
        if (unread.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var row in unread) row.ReadAt = now;

        await _db.SaveChangesAsync(ct);
        _realtime.NotifyNotificationsChanged();
        return unread.Count;
    }

    /// <summary>
    /// Deleting belongs on the read side even though the write side lives elsewhere: this is a
    /// user acting on a notice they are done with, not an event the ledger produced, so there is
    /// no business transaction to share.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row is null) return false;

        _db.Notifications.Remove(row);
        await _db.SaveChangesAsync(ct);
        _realtime.NotifyNotificationsChanged();

        return true;
    }

    public async Task<int> DeleteReadAsync(CancellationToken ct)
    {
        var read = await _db.Notifications.Where(n => n.ReadAt != null).ToListAsync(ct);

        // Same discipline as MarkAllReadAsync: nothing changed, nothing announced. A signal here
        // would send every open panel back to the server to rediscover the list it already has.
        if (read.Count == 0) return 0;

        _db.Notifications.RemoveRange(read);
        await _db.SaveChangesAsync(ct);
        _realtime.NotifyNotificationsChanged();

        return read.Count;
    }
}
