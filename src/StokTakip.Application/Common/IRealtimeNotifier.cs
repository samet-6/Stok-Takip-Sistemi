namespace StokTakip.Application.Common;

/// <summary>
/// Server→client invalidation signals. Framework-free by design: the SignalR
/// implementation lives in the Api layer, so neither Application nor Infrastructure
/// ever references SignalR.
/// <para>
/// Every method returns <c>void</c> deliberately — a caller physically cannot await a
/// signal and hold a business transaction open behind a hub dispatch.
/// </para>
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Announces that a product changed — stock movement, edit, creation or deletion.
    /// Carries the id only, never the new values: receivers refetch over REST, which
    /// re-applies authorization instead of trusting a broadcast payload.
    /// </summary>
    /// <remarks>
    /// Must be called AFTER the transaction commits. Signalling first makes listeners
    /// read the OLD row — push would then leave screens staler than no push at all.
    /// </remarks>
    void NotifyProductChanged(int productId);

    /// <summary>
    /// Announces that the notification list changed. Carries nothing — not even a count:
    /// the receiver refetches, which is also what re-applies the Admin-only authorization.
    /// </summary>
    /// <remarks>
    /// Delivered to admins only, not <c>Clients.All</c>. A Çalışan cannot read these rows, so
    /// telling them "something happened" would leak a fact they have no endpoint to act on.
    /// Same post-commit rule as above.
    /// </remarks>
    void NotifyNotificationsChanged();
}

/// <summary>
/// Hub method names. Mirrored by hand in the TypeScript client — a signal renamed here
/// and not there fails silently, so treat these strings as a contract.
/// </summary>
public static class RealtimeEvents
{
    public const string ProductChanged = "ProductChanged";
    public const string NotificationsChanged = "NotificationsChanged";

    /// <summary>SignalR group holding every connected admin — the audience for notifications.</summary>
    public const string AdminGroup = "admins";
}
