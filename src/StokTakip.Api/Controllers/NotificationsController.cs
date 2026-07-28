using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokTakip.Application.Notifications;

namespace StokTakip.Api.Controllers;

/// <summary>
/// Admin-only by design, not by omission: every notification kind in this domain is an admin
/// concern. A Çalışan gets 403 here.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Roles = "Admin")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
        => _notificationService = notificationService;

    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> GetPaged(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await _notificationService.GetPagedAsync(page, pageSize, ct));

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
        => await _notificationService.MarkReadAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notificationService.MarkAllReadAsync(ct);
        return NoContent();
    }
}
