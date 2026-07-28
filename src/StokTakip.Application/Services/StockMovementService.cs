using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.StockMovements;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;

namespace StokTakip.Application.Services;

public sealed class StockMovementService : IStockMovementService
{
    // A ceiling, not a target: a collision needs a second movement on the same product inside
    // the few milliseconds between this request's read and its write. Losing that race three
    // times in a row is real contention, and the caller should hear about it (409) rather than
    // have the request spin.
    private const int MaxSaveAttempts = 3;

    private readonly IAppDbContext _db;
    private readonly IUserLookupService _userLookup;
    private readonly IRealtimeNotifier _realtime;

    public StockMovementService(
        IAppDbContext db, IUserLookupService userLookup, IRealtimeNotifier realtime)
    {
        _db = db;
        _userLookup = userLookup;
        _realtime = realtime;
    }

    public async Task<PagedResult<StockMovementDto>> GetPagedAsync(
        StockMovementQuery query, CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.StockMovements.AsNoTracking();
        if (query.ProductId is int pid)
            q = q.Where(m => m.ProductId == pid);
        if (query.Type is StockMovementType movementType)
            q = q.Where(m => m.Type == movementType);
        // "Yapan" filter. The controller decides the value: a Çalışan is forced to their
        // own id (can't read others'), an Admin may pass any id or none (see all).
        if (!string.IsNullOrEmpty(query.UserId))
            q = q.Where(m => m.CreatedByUserId == query.UserId);
        // Supplier/Category narrow by the movement's product (join through Product).
        if (query.SupplierId is int supplierId)
            q = q.Where(m => m.Product.SupplierId == supplierId);
        if (query.CategoryId is int categoryId)
            q = q.Where(m => m.Product.CategoryId == categoryId);
        // CreatedAt range, inclusive on both ends. CreatedAt is a timestamptz (UTC) column;
        // the backend only compares instants and stays timezone-agnostic. The frontend, which
        // knows the viewer's timezone, sends offset-aware ISO boundaries (e.g. the local day
        // start as ...+03:00), so the comparison below runs UTC-vs-UTC.
        if (query.From is DateTime from)
        {
            var fromUtc = ToUtc(from);
            q = q.Where(m => m.CreatedAt >= fromUtc);
        }
        if (query.To is DateTime to)
        {
            var toUtc = ToUtc(to);
            q = q.Where(m => m.CreatedAt <= toUtc);
        }

        var totalCount = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id, m.ProductId, ProductName = m.Product.Name,
                m.Type, m.Quantity, m.Note, m.CreatedAt, m.CreatedByUserId
            })
            .ToListAsync(ct);

        var names = await _userLookup.GetFullNamesAsync(
            rows.Select(r => r.CreatedByUserId), ct);

        var items = rows
            .Select(r => new StockMovementDto(
                r.Id, r.ProductId, r.ProductName, r.Type, r.Quantity, r.Note, r.CreatedAt,
                r.CreatedByUserId,
                names.TryGetValue(r.CreatedByUserId, out var fullName) ? fullName : string.Empty))
            .ToList();

        return new PagedResult<StockMovementDto>(items, page, pageSize, totalCount);
    }

    public async Task<StockMovementResponse> CreateAsync(
        CreateStockMovementRequest request, string userId, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw new BadRequestException("Ürün bulunamadı");

        await EnsureMovementAllowedAsync(product, request, userId, ct);

        var movement = new StockMovement
        {
            ProductId = product.Id,
            Type = request.Type,
            Quantity = request.Quantity,
            Note = request.Note,
            CreatedByUserId = userId
        };
        _db.StockMovements.Add(movement);

        var delta = request.Type == StockMovementType.In ? request.Quantity : -request.Quantity;

        // Movement insert + StockQuantity update in a SINGLE SaveChangesAsync — one implicit
        // transaction, so either both land or neither does; no explicit BeginTransaction needed.
        //
        // The product's xmin is a concurrency token, so a movement that another request beats by
        // milliseconds finds a stale token and its UPDATE matches no rows. That is not a conflict
        // worth reporting: both movements are real events and both belong in the ledger, so the
        // row is reloaded, the rules are judged again against the committed quantity, and the
        // delta is applied on top. Rejecting the second one would push a valid entry back onto
        // the user; dropping the token instead would let StockQuantity drift away from the ledger
        // (two concurrent Out movements would each write their own arithmetic).
        //
        // Retrying here cannot double-post: the failed SaveChangesAsync rolled its whole
        // transaction back, so the previous attempt's INSERT provably never landed. A retry from
        // the CLIENT is a different matter — it cannot know whether the first request committed,
        // which is why the caller is asked to check and re-send rather than resending on its own.
        Notification? stagedNotification = null;

        for (var attempt = 1; ; attempt++)
        {
            var previousQuantity = product.StockQuantity;
            product.StockQuantity += delta;

            // Threshold detection needs the quantity this attempt actually started from, and a
            // retry reloads that value — so the notification is staged inside the loop, next to
            // the arithmetic it describes. Staging it once outside would describe a crossing that
            // never happened on the attempt that finally committed.
            stagedNotification = StageThresholdNotification(product, previousQuantity, userId);

            try
            {
                // One SaveChanges = one transaction: the movement, the new StockQuantity and the
                // notification land together or not at all. That atomicity is the whole delivery
                // guarantee — no outbox, no acks, because the database IS the queue.
                await _db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
            {
                // Reload drops our pending quantity change and brings the committed row with its
                // new token.
                await _db.Entry(product).ReloadAsync(ct);

                // Everything this request staged has to be dropped before the rules are re-judged:
                // if the fresh quantity turns a valid Out into an invalid one, the rejection notice
                // gets its own SaveChanges, and an attached movement would ride along with it.
                Detach(movement);
                Detach(stagedNotification);
                stagedNotification = null;

                await EnsureMovementAllowedAsync(product, request, userId, ct);

                // Still allowed — the movement goes back on for the next attempt. Its Id is still
                // 0 (nothing ever committed), so this re-inserts rather than duplicating.
                _db.StockMovements.Add(movement);
            }
        }

        // After the commit, never before: a listener that refetches on an uncommitted signal
        // reads the old StockQuantity and ends up staler than if we had said nothing.
        _realtime.NotifyProductChanged(product.Id);

        // Only when a row was actually written — the signal follows the notification, not the
        // request, so an ordinary movement does not wake every admin's bell.
        if (stagedNotification is not null)
            _realtime.NotifyNotificationsChanged();

        var names = await _userLookup.GetFullNamesAsync([userId], ct);
        var dto = new StockMovementDto(
            movement.Id, product.Id, product.Name, movement.Type, movement.Quantity,
            movement.Note, movement.CreatedAt, userId,
            names.TryGetValue(userId, out var fullName) ? fullName : string.Empty);

        return new StockMovementResponse(dto, product.StockQuantity);
    }

    // Shared by the first attempt and every retry: a reload brings a different quantity, so the
    // same rules have to be judged again on the fresh row — a valid Out can become an invalid one.
    private async Task EnsureMovementAllowedAsync(
        Product product, CreateStockMovementRequest request, string userId, CancellationToken ct)
    {
        // A passive product is out of the ledger entirely — neither direction.
        // The alternative, allowing Out so leftover stock could be drained, was rejected: it
        // leaves "can I move this?" depending on the direction, which nobody remembers. Cost
        // accepted deliberately: deactivating a product with stock freezes that stock, and the
        // way out is to reactivate it (soft delete is not a one-way door), drain, deactivate.
        // No notification: a frozen product is a catalog decision the admin already made, not a
        // sign that stock is missing.
        if (!product.IsActive)
            throw new BadRequestException("Ürün pasif; stok hareketi için önce ürünü aktifleştirin.");

        if (request.Type != StockMovementType.Out || request.Quantity <= product.StockQuantity)
            return;

        await RecordRejectedOutAsync(product, request, userId, ct);
        throw new BadRequestException($"Yetersiz stok. Mevcut: {product.StockQuantity}");
    }

    /// <summary>
    /// Writes the "somebody could not find the stock they expected" notice. This is the one
    /// notification type that cannot share a business transaction — a refused movement changes
    /// nothing, so there is no transaction to share. Its invariant is the mirror image and just
    /// as clean: the row exists exactly when a rejection happened.
    /// </summary>
    private async Task RecordRejectedOutAsync(
        Product product, CreateStockMovementRequest request, string userId, CancellationToken ct)
    {
        // De-duplication, and the table is its own state — same trick as threshold detection.
        // Without it a client retrying "take out 9999" would write a row per attempt, and there
        // is no rate limiting anywhere in this project to stop it. One unread notice per product
        // says everything a second one would.
        var alreadyPending = await _db.Notifications.AnyAsync(
            n => n.Type == NotificationType.RejectedOutMovement
                 && n.ProductId == product.Id
                 && n.ReadAt == null, ct);

        if (alreadyPending) return;

        _db.Notifications.Add(new Notification
        {
            Type = NotificationType.RejectedOutMovement,
            ProductId = product.Id,
            Quantity = product.StockQuantity,       // what was actually on hand
            RequestedQuantity = request.Quantity,   // what was asked for
            CreatedByUserId = userId
        });

        await _db.SaveChangesAsync(ct);
        _realtime.NotifyNotificationsChanged();
    }

    /// <summary>
    /// Stages a threshold-crossing notice, or nothing when no edge was crossed. Returns the
    /// staged row so a retry can drop it — nothing is saved here; the caller's SaveChanges is
    /// what makes the notification atomic with the movement that caused it.
    /// </summary>
    private Notification? StageThresholdNotification(
        Product product, int previousQuantity, string userId)
    {
        // Edge detection, not level detection: 39 → 41 → 39 produces one notification, not two,
        // because only the crossing fires. No extra state is needed — the transaction that
        // updates StockQuantity is holding the previous value already.
        var type = DetectThresholdCrossing(product, previousQuantity);
        if (type is null) return null;

        var notification = new Notification
        {
            Type = type.Value,
            ProductId = product.Id,
            Quantity = product.StockQuantity,
            CreatedByUserId = userId
        };

        _db.Notifications.Add(notification);
        return notification;
    }

    private static NotificationType? DetectThresholdCrossing(Product product, int previousQuantity)
    {
        // Zero is also "below minimum", so the two rules would both match at once. OutOfStock
        // wins and is the only row written: the urgent phrasing carries the other one's meaning,
        // and two rows for one event would read as two problems.
        if (previousQuantity > 0 && product.StockQuantity == 0)
            return NotificationType.OutOfStock;

        if (previousQuantity >= product.MinStockLevel && product.StockQuantity < product.MinStockLevel)
            return NotificationType.LowStock;

        return null;
    }

    /// <summary>Drops a pending insert so an abandoned attempt cannot ride the next SaveChanges.</summary>
    private void Detach(object? entity)
    {
        if (entity is not null) _db.Entry(entity).State = EntityState.Detached;
    }

    // Normalizes a filter bound to a UTC instant for the timestamptz comparison. Offset-aware
    // input (the expected path: frontend sends the local day boundary with its offset) arrives
    // as Local/Utc and is converted; an offset-less value (Unspecified) is a defensive fallback
    // treated as already-UTC so Npgsql accepts it.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
