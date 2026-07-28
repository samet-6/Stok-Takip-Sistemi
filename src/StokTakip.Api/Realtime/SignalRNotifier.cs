using Microsoft.AspNetCore.SignalR;
using StokTakip.Application.Common;

namespace StokTakip.Api.Realtime;

/// <summary>
/// Bridges the framework-free <see cref="IRealtimeNotifier"/> to SignalR.
/// <para>
/// Every send is started and abandoned: a slow or broken hub must never delay a business
/// transaction, and must never fail one either — a stock movement that was committed
/// stays committed even if nobody hears about it. Failures are logged and swallowed;
/// the client's next reconnect refetches everything anyway.
/// </para>
/// Safe as a singleton — <see cref="IHubContext{THub}"/> is thread-safe and lifetime-free.
/// </summary>
public sealed class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<StokHub> _hub;
    private readonly ILogger<SignalRNotifier> _logger;

    public SignalRNotifier(IHubContext<StokHub> hub, ILogger<SignalRNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    // Clients.All is deliberate: every hub connection is authenticated, and reading products
    // is open to every signed-in user, so there is nothing here to scope per-user. The payload
    // is an id, not the product — a receiver still has to go through REST to learn anything.
    public void NotifyProductChanged(int productId)
        => Dispatch(RealtimeEvents.ProductChanged, _hub.Clients.All, productId);

    // Group, not All: only admins can read notifications, so only admins are told they changed.
    // No payload — not even a count; the badge number comes back with the refetch, which is also
    // where the Admin role is checked.
    public void NotifyNotificationsChanged()
        => Dispatch(
            RealtimeEvents.NotificationsChanged,
            _hub.Clients.Group(RealtimeEvents.AdminGroup),
            null);

    // Discards the task on purpose: the caller returns immediately and the continuation
    // below owns every failure path, so nothing can surface as an unobserved exception.
    private void Dispatch(string eventName, IClientProxy target, object? arg)
        => _ = SendAsync(eventName, target, arg);

    private async Task SendAsync(string eventName, IClientProxy target, object? arg)
    {
        try
        {
            await target.SendAsync(eventName, arg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime signal {Event} could not be delivered.", eventName);
        }
    }
}
