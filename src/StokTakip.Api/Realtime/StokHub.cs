using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StokTakip.Application.Common;

namespace StokTakip.Api.Realtime;

/// <summary>
/// Server→client only: there are deliberately no client-callable methods. Everything the
/// server sends is an invalidation hint ("product 5 changed"), never data — the client
/// refetches over REST, where authorization is already enforced. So the hub needs no
/// inbound surface at all, and not having one means there is none to secure.
/// </summary>
[Authorize]
public sealed class StokHub : Hub
{
    private readonly ILogger<StokHub> _logger;

    public StokHub(ILogger<StokHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Hub connected: user {UserId}, connection {ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);

        // Admins join a group so notification signals reach only them. Membership is decided
        // from the ticket's own role claim — the client never asks to join, so there is no
        // inbound method to abuse. Leaving is automatic on disconnect.
        if (Context.User?.IsInRole("Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeEvents.AdminGroup);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // exception is null on a clean stop, set on a dropped connection — one log line
        // covers both, and the reconnect that follows shows up as a new connect entry.
        _logger.LogInformation(
            exception,
            "Hub disconnected: user {UserId}, connection {ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }
}
