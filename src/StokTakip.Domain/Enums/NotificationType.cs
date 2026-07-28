namespace StokTakip.Domain.Enums;

/// <summary>
/// Admin-facing notification kinds. Values are persisted as integers and mirrored by a
/// check constraint, so existing numbers must never be reused for a different meaning.
/// </summary>
public enum NotificationType
{
    /// <summary>A movement pushed the product below its minimum stock level.</summary>
    LowStock = 1,

    /// <summary>A movement took the product to zero. Supersedes <see cref="LowStock"/>.</summary>
    OutOfStock = 2,

    /// <summary>An Out movement was refused because the product did not hold enough stock.</summary>
    RejectedOutMovement = 3
}
