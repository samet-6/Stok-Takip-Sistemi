using StokTakip.Domain.Enums;

namespace StokTakip.Application.Notifications;

/// <summary>
/// A notification as the bell panel needs it. Carries the product name so the panel does not
/// have to join anything client-side, and the ids so a click can navigate to the product.
/// </summary>
public sealed record NotificationDto(
    int Id,
    NotificationType Type,
    int ProductId,
    string ProductName,
    int Quantity,
    int? RequestedQuantity,
    DateTime CreatedAt,
    string CreatedByUserId,
    string CreatedByFullName,
    DateTime? ReadAt);

/// <summary>Page of notifications plus the unread total, which the badge needs on every fetch.</summary>
public sealed record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int UnreadCount);
