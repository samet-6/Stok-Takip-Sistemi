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
    private readonly IRealtimeNotifier _realtime;

    public ProductService(
        IAppDbContext db, IUserLookupService userLookup, IRealtimeNotifier realtime)
    {
        _db = db;
        _userLookup = userLookup;
        _realtime = realtime;
    }

    public async Task<PagedResult<ProductListDto>> GetPagedAsync(ProductQuery query, CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = ApplyScope(_db.Products.AsNoTracking(), query.Scope);

        if (!query.IncludeInactive)
            q = q.Where(p => p.IsActive);
        if (query.LowStockOnly)
            q = q.Where(p => p.StockQuantity <= p.MinStockLevel);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // The term is folded by the database (SearchText.Fold -> f_fold), by the very
            // function that generated the columns it is compared against, so "cop", "çöp" and
            // "ÇÖP" all reduce to one key. Folding it here in C# is what used to break: the term
            // went through .NET's culture-sensitive ToLower() and the column through SQL lower()
            // under the database ctype, so the two rules never met — and the result even changed
            // between the dev database and the container.
            //
            // LIKE rather than Contains(): EF turns Contains() with a non-constant argument into
            // strpos(...) > 0, which the trigram index cannot serve. Wildcards the user typed are
            // escaped first; f_fold leaves % _ \ untouched.
            //
            // Category/Supplier names join through the (required) navigations, so "elektronik"
            // still matches products by their category or supplier.
            var pattern = SearchText.ContainsPattern(query.Search);

            q = q.Where(p =>
                EF.Functions.Like(EF.Property<string>(p, SearchText.NameFolded), SearchText.Fold(pattern)) ||
                EF.Functions.Like(EF.Property<string>(p, SearchText.SkuFolded), SearchText.Fold(pattern)) ||
                EF.Functions.Like(EF.Property<string>(p.Category, SearchText.NameFolded), SearchText.Fold(pattern)) ||
                EF.Functions.Like(EF.Property<string>(p.Supplier, SearchText.NameFolded), SearchText.Fold(pattern)));
        }

        var totalCount = await q.CountAsync(ct);

        var items = await q
            // Name is not unique, so it cannot order rows on its own: without a tie-breaker
            // equal names may land in a different relative order per query, which lets a row
            // repeat on one page and vanish from another. Id makes the sort total.
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductListDto(
                p.Id, p.Name, p.SKU, p.CategoryId, p.Category.Name, p.SupplierId, p.Supplier.Name,
                p.UnitPrice, p.StockQuantity, p.MinStockLevel, p.IsActive,
                EF.Property<uint>(p, "xmin"), p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<ProductListDto>(items, page, pageSize, totalCount);
    }

    public async Task<ProductSummaryDto> GetSummaryAsync(ProductScope scope, CancellationToken ct)
    {
        // One round trip: COUNT/SUM run inside PostgreSQL over the whole scope, so the
        // numbers are exact regardless of how many products match, and money is summed as
        // numeric rather than accumulated in the client's floating-point arithmetic.
        // Materialised as a list rather than with FirstOrDefault, and the reason is only about
        // the log: grouping on a constant collapses the scope to at most one row, so "which row"
        // has a single possible answer — but EF's check for that warning is syntactic (is there
        // an OrderBy or a filter next to the First?), not cardinality-aware, so it flagged every
        // summary call as potentially unpredictable. Taking the list keeps the same single-row
        // aggregate without a row-limiting operator to warn about.
        var rows = await ApplyScope(_db.Products.AsNoTracking(), scope)
            .GroupBy(_ => 1)
            .Select(g => new ProductSummaryDto(
                g.Count(),
                g.Count(p => p.IsActive),
                g.Count(p => !p.IsActive),
                g.Count(p => p.IsActive && p.StockQuantity <= p.MinStockLevel),
                g.Sum(p => p.IsActive ? p.UnitPrice * p.StockQuantity : 0m)))
            .ToListAsync(ct);

        // An empty scope produces no group at all.
        return rows.Count == 0 ? new ProductSummaryDto(0, 0, 0, 0, 0m) : rows[0];
    }

    // Narrows products to a catalog dimension. Shared by the list and the summary so the
    // two can never disagree about which products they are describing.
    private static IQueryable<Product> ApplyScope(IQueryable<Product> q, ProductScope scope)
    {
        if (scope.CategoryId is int categoryId)
            q = q.Where(p => p.CategoryId == categoryId);
        if (scope.SupplierId is int supplierId)
            q = q.Where(p => p.SupplierId == supplierId);

        return q;
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
                StockValue = x.UnitPrice * x.StockQuantity,
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
            p.UnitPrice, p.StockQuantity, p.MinStockLevel, p.StockValue, p.IsActive,
            p.RowVersion, p.CreatedAt, p.UpdatedAt, p.Description, movements);
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

        // Post-commit (see IRealtimeNotifier): a new product changes the list and the summary
        // tiles, and an initial stock movement changes them again.
        _realtime.NotifyProductChanged(product.Id);

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
        // later went inactive can still be edited.
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

        var written = await _db.SaveChangesAsync(ct);

        // The signal follows the row, not the request. Two cases produce no signal, both right:
        // a stale RowVersion throws above (409 — a rejected edit changed nothing), and a PUT
        // that re-sends identical values makes EF's change tracker emit no UPDATE at all.
        // Broadcasting on the latter would have every open screen refetch identical data.
        if (written > 0)
            _realtime.NotifyProductChanged(id);

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
            _realtime.NotifyProductChanged(id); // a row that vanished is a change like any other
            return null; // hard-deleted
        }

        // A product with movements is never hard-deleted → soft delete (append-only ledger).
        product.IsActive = false;
        // Same rule as UpdateAsync: deleting an already-inactive product writes nothing.
        if (await _db.SaveChangesAsync(ct) > 0)
            _realtime.NotifyProductChanged(id);

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
