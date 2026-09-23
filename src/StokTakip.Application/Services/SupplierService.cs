using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Application.Suppliers;
using StokTakip.Domain.Entities;

namespace StokTakip.Application.Services;

public sealed class SupplierService : ISupplierService
{
    private readonly IAppDbContext _db;

    public SupplierService(IAppDbContext db) => _db = db;

    /// <summary>
    /// The one definition of what a SupplierDto is — see the note on CategoryService.ToDto.
    /// Contact fields are redacted per role by the controller, not here: this shape is what the
    /// database returns, and hiding a field is an authorization decision, not a mapping one.
    /// </summary>
    private static readonly Expression<Func<Supplier, SupplierDto>> ToDto =
        s => new SupplierDto(
            s.Id, s.Name, s.ContactEmail, s.Phone, s.Address, s.IsActive, s.DeactivatedAt,
            s.Products.Count, s.RowVersion, s.CreatedAt, s.UpdatedAt);

    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct)
        => await _db.Suppliers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(ToDto)
            .ToListAsync(ct);

    public async Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var dto = await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("Tedarikçi bulunamadı");
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct)
    {
        var supplier = new Supplier
        {
            Name = CleanName(request.Name),
            ContactEmail = request.ContactEmail,
            Phone = request.Phone,
            Address = request.Address,
            IsActive = true
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);

        // Read back through the one projection instead of assembling a second DTO by hand
        // (the pattern ProductService already uses). Hand-assembly had to invent ProductCount —
        // a literal 0 that is true only because a supplier cannot be born with products.
        return (await GetByIdAsync(supplier.Id, ct))!;
    }

    public async Task UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Tedarikçi bulunamadı");

        supplier.Name = CleanName(request.Name);
        supplier.ContactEmail = request.ContactEmail;
        supplier.Phone = request.Phone;
        supplier.Address = request.Address;
        supplier.IsActive = request.IsActive;

        // Optimistic concurrency, as in ProductService: a stale RowVersion makes the UPDATE match
        // 0 rows, raising DbUpdateConcurrencyException (→ 409 concurrency_conflict).
        _db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = request.RowVersion;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// No uniqueness for supplier names (D9c), but a trailing zero-width space still sorts and
    /// searches differently from the name it looks identical to — so the same cleaning applies.
    /// </summary>
    private static string CleanName(string raw)
    {
        var name = NameText.Clean(raw);
        return name.Length > 0 ? name : throw new BadRequestException("Tedarikçi adı boş olamaz");
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Tedarikçi bulunamadı");

        var productCount = await _db.Products.CountAsync(p => p.SupplierId == id, ct);
        if (productCount > 0)
            throw new ConflictException(
                $"Bu tedarikçiye bağlı {productCount} ürün var, önce ürünleri taşıyın/silin.");

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(ct);
    }
}
