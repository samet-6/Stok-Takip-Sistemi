using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.StockMovements;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;

namespace StokTakip.Application.Services;

public sealed class StockMovementService : IStockMovementService
{
    private readonly IAppDbContext _db;
    private readonly IUserLookupService _userLookup;

    public StockMovementService(IAppDbContext db, IUserLookupService userLookup)
    {
        _db = db;
        _userLookup = userLookup;
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

        // Movements are allowed on inactive products too (draining leftover stock).
        if (request.Type == StockMovementType.Out && request.Quantity > product.StockQuantity)
            throw new BadRequestException($"Yetersiz stok. Mevcut: {product.StockQuantity}");

        var movement = new StockMovement
        {
            ProductId = product.Id,
            Type = request.Type,
            Quantity = request.Quantity,
            Note = request.Note,
            CreatedByUserId = userId
        };
        _db.StockMovements.Add(movement);

        // Movement insert + StockQuantity update in a SINGLE SaveChangesAsync — one implicit
        // transaction, so either both land or neither does; no explicit BeginTransaction needed.
        // If another request updates this product first, the stale xmin token makes the UPDATE
        // match no rows and the caller gets a 409. Re-sending is left to the user: movements
        // carry no idempotency key, so an automatic retry could double-post one.
        product.StockQuantity += request.Type == StockMovementType.In
            ? request.Quantity
            : -request.Quantity;

        await _db.SaveChangesAsync(ct);

        var names = await _userLookup.GetFullNamesAsync([userId], ct);
        var dto = new StockMovementDto(
            movement.Id, product.Id, product.Name, movement.Type, movement.Quantity,
            movement.Note, movement.CreatedAt, userId,
            names.TryGetValue(userId, out var fullName) ? fullName : string.Empty);

        return new StockMovementResponse(dto, product.StockQuantity);
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
