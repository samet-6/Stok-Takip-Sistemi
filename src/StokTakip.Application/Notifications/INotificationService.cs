namespace StokTakip.Application.Notifications;

public interface INotificationService
{
    Task<NotificationListResponse> GetPagedAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>Marks one notification read. Already-read rows are left alone (idempotent).</summary>
    Task<bool> MarkReadAsync(int id, CancellationToken ct);

    /// <summary>Marks every unread notification read; returns how many rows changed.</summary>
    Task<int> MarkAllReadAsync(CancellationToken ct);

    /// <summary>Removes one notification. False when there is no such row — unlike marking read,
    /// this is not idempotent: once the row is gone there is nothing at that address.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);

    /// <summary>Removes every notification already read; returns how many rows went.</summary>
    Task<int> DeleteReadAsync(CancellationToken ct);
}
