using StokTakip.Domain.Common;
using StokTakip.Domain.Enums;

namespace StokTakip.Domain.Entities;

/// <summary>
/// An admin-facing event worth persisting. Append-only in practice: the only mutation is
/// <see cref="ReadAt"/>.
/// <para>
/// There is no target-user column. Every notification here is for the admin, and the system
/// has exactly one (a second can only arrive through the seeder), so "who is this for?" has
/// a single answer. Read state therefore lives on the row itself rather than in a per-user table.
/// </para>
/// </summary>
public class Notification : IHasCreatedAt
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public int ProductId { get; set; }

    /// <summary>Stock at the moment of the event; for a rejection, what was actually on hand.</summary>
    public int Quantity { get; set; }

    /// <summary>Only set for <see cref="NotificationType.RejectedOutMovement"/>: the amount asked for.</summary>
    public int? RequestedQuantity { get; set; }

    /// <summary>Whoever caused the event — the point of a rejection notice is knowing who came up short.</summary>
    public required string CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null while unread. The bell counts rows where this is null.</summary>
    public DateTime? ReadAt { get; set; }

    public Product Product { get; set; } = null!;
}
