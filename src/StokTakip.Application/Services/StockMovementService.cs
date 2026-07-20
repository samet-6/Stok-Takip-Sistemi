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
        int? productId, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.StockMovements.AsNoTracking();
        if (productId is int pid)
            q = q.Where(m => m.ProductId == pid);

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
        // transaction, atomic. A concurrent race trips the product's xmin token → 409, and the
        // client retries; no explicit BeginTransaction needed.
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
}
