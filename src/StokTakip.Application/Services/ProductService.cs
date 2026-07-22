using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.Products;
using StokTakip.Application.StockMovements;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;

namespace StokTakip.Application.Services;

public sealed class ProductService : IProductService
{
    private const string InitialStockNote = "Başlangıç stoğu";

    private readonly IAppDbContext _db;
    private readonly IUserLookupService _userLookup;

    public ProductService(IAppDbContext db, IUserLookupService userLookup)
    {
        _db = db;
        _userLookup = userLookup;
    }

    public async Task<PagedResult<ProductListDto>> GetPagedAsync(ProductQuery query, CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.Products.AsNoTracking();

        if (!query.IncludeInactive)
            q = q.Where(p => p.IsActive);
        if (query.CategoryId is int categoryId)
            q = q.Where(p => p.CategoryId == categoryId);
        if (query.SupplierId is int supplierId)
            q = q.Where(p => p.SupplierId == supplierId);
        if (query.LowStockOnly)
            q = q.Where(p => p.StockQuantity <= p.MinStockLevel);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Provider-agnostic: EF translates ToLower()+Contains to LOWER(...) LIKE.
            // Category/Supplier names join through the (required) navigations so a search
            // like "elektronik" also matches products by their category/supplier.
            var term = query.Search.Trim().ToLower();
            q = q.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.SKU.ToLower().Contains(term) ||
                p.Category.Name.ToLower().Contains(term) ||
                p.Supplier.Name.ToLower().Contains(term));
        }

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductListDto(
                p.Id, p.Name, p.SKU, p.CategoryId, p.Category.Name, p.SupplierId, p.Supplier.Name,
                p.UnitPrice, p.StockQuantity, p.MinStockLevel, p.IsActive,
                EF.Property<uint>(p, "xmin"), p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<ProductListDto>(items, page, pageSize, totalCount);
    }

    public async Task<ProductDetailDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var p = await _db.Products
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Name, x.SKU, x.CategoryId,
                CategoryName = x.Category.Name,
                x.SupplierId,
                SupplierName = x.Supplier.Name,
                x.UnitPrice, x.StockQuantity, x.MinStockLevel, x.IsActive,
                RowVersion = EF.Property<uint>(x, "xmin"),
                x.CreatedAt, x.UpdatedAt, x.Description,
                Movements = x.Movements
                    .OrderByDescending(m => m.CreatedAt)
                    .ThenByDescending(m => m.Id)
                    .Take(10)
                    .Select(m => new
                    {
                        m.Id, m.ProductId, m.Type, m.Quantity, m.Note, m.CreatedAt, m.CreatedByUserId
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (p is null)
            throw new NotFoundException("Ürün bulunamadı");

        var names = await _userLookup.GetFullNamesAsync(
            p.Movements.Select(m => m.CreatedByUserId), ct);

        var movements = p.Movements
            .Select(m => new StockMovementDto(
                m.Id, m.ProductId, p.Name, m.Type, m.Quantity, m.Note, m.CreatedAt,
                m.CreatedByUserId,
                names.TryGetValue(m.CreatedByUserId, out var fullName) ? fullName : string.Empty))
            .ToList();

        return new ProductDetailDto(
            p.Id, p.Name, p.SKU, p.CategoryId, p.CategoryName, p.SupplierId, p.SupplierName,
            p.UnitPrice, p.StockQuantity, p.MinStockLevel, p.IsActive, p.RowVersion,
            p.CreatedAt, p.UpdatedAt, p.Description, movements);
    }

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request, string userId, CancellationToken ct)
    {
        var sku = request.SKU.Trim().ToUpperInvariant();

        if (await _db.Products.AnyAsync(p => p.SKU == sku, ct))
            throw new ConflictException("Bu SKU zaten kayıtlı");

        await ValidateCategoryAsync(request.CategoryId, ct);
        var supplier = await GetSupplierOrThrowAsync(request.SupplierId, ct);
        if (!supplier.IsActive)
            throw new BadRequestException("Pasif tedarikçiye ürün atanamaz");

        var product = new Product
        {
            Name = request.Name,
            SKU = sku,
            Description = request.Description,
            CategoryId = request.CategoryId,
            SupplierId = request.SupplierId,
            UnitPrice = request.UnitPrice,
            MinStockLevel = request.MinStockLevel,
            StockQuantity = 0,
            IsActive = true
        };
        _db.Products.Add(product);

        // Product born with 0 stock; optional initial stock produces an In movement in the
        // SAME SaveChangesAsync — a single SaveChanges is one implicit transaction, so no
        // explicit BeginTransaction is needed for atomicity.
        if (request.InitialStock is int qty && qty > 0)
        {
            product.StockQuantity = qty;
            product.Movements.Add(new StockMovement
            {
                Type = StockMovementType.In,
                Quantity = qty,
                Note = InitialStockNote,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync(ct);

        return (await GetByIdAsync(product.Id, ct))!;
    }

    public async Task<ProductDetailDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Ürün bulunamadı");

        var sku = request.SKU.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(p => p.Id != id && p.SKU == sku, ct))
            throw new ConflictException("Bu SKU zaten kayıtlı");

        await ValidateCategoryAsync(request.CategoryId, ct);

        var supplierChanged = product.SupplierId != request.SupplierId;
        var supplier = await GetSupplierOrThrowAsync(request.SupplierId, ct);
        // Active-check only when the supplier actually changes: a product whose supplier
        // later went inactive can still be edited (veri_modeli refinement).
        if (supplierChanged && !supplier.IsActive)
            throw new BadRequestException("Pasif tedarikçiye ürün atanamaz");

        product.Name = request.Name;
        product.SKU = sku;
        product.Description = request.Description;
        product.CategoryId = request.CategoryId;
        product.SupplierId = request.SupplierId;
        product.UnitPrice = request.UnitPrice;
        product.MinStockLevel = request.MinStockLevel;
        product.IsActive = request.IsActive;
        // Stock is never touched here — it changes only through movements.

        // Optimistic concurrency: a stale RowVersion makes the UPDATE match 0 rows,
        // raising DbUpdateConcurrencyException (→ 409).
        _db.Entry(product).Property("xmin").OriginalValue = request.RowVersion;

        await _db.SaveChangesAsync(ct);

        return (await GetByIdAsync(id, ct))!;
    }

    public async Task<ProductListDto?> DeleteAsync(int id, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Ürün bulunamadı");

        var hasMovements = await _db.StockMovements.AnyAsync(m => m.ProductId == id, ct);

        if (!hasMovements)
        {
            _db.Products.Remove(product);
            await _db.SaveChangesAsync(ct);
            return null; // hard-deleted
        }

        // A product with movements is never hard-deleted → soft delete (append-only ledger).
        product.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductListDto(
                p.Id, p.Name, p.SKU, p.CategoryId, p.Category.Name, p.SupplierId, p.Supplier.Name,
                p.UnitPrice, p.StockQuantity, p.MinStockLevel, p.IsActive,
                EF.Property<uint>(p, "xmin"), p.CreatedAt, p.UpdatedAt))
            .FirstAsync(ct);
    }

    private async Task ValidateCategoryAsync(int categoryId, CancellationToken ct)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new BadRequestException("Kategori bulunamadı");
    }

    private async Task<Supplier> GetSupplierOrThrowAsync(int supplierId, CancellationToken ct)
        => await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new BadRequestException("Tedarikçi bulunamadı");
}
