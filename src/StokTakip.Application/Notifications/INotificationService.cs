namespace StokTakip.Application.Notifications;

public interface INotificationService
{
    Task<NotificationListResponse> GetPagedAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>Marks one notification read. Already-read rows are left alone (idempotent).</summary>
    Task<bool> MarkReadAsync(int id, CancellationToken ct);

    /// <summary>Marks every unread notification read; returns how many rows changed.</summary>
    Task<int> MarkAllReadAsync(CancellationToken ct);
}
