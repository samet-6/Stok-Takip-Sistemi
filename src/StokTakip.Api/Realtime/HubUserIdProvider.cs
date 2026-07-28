using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace StokTakip.Api.Realtime;

/// <summary>
/// Resolves a connection's user id from the raw <c>sub</c> claim.
/// <para>
/// Not optional: SignalR's default provider reads <c>ClaimTypes.NameIdentifier</c>, and
/// this API sets <c>MapInboundClaims = false</c>, so nothing ever populates that claim.
/// Without this, <c>Clients.User(id)</c> compiles, runs, reports no error — and reaches
/// nobody.
/// </para>
/// </summary>
public sealed class HubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirstValue("sub");
}
