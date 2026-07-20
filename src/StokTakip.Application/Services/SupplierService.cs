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

    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct)
        => await _db.Suppliers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.ContactEmail, s.Phone, s.Address, s.IsActive,
                s.Products.Count, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(ct);

    public async Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var dto = await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.ContactEmail, s.Phone, s.Address, s.IsActive,
                s.Products.Count, s.CreatedAt, s.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("Tedarikçi bulunamadı");
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct)
    {
        if (await _db.Suppliers.AnyAsync(s => s.Name == request.Name, ct))
            throw new ConflictException("Bu tedarikçi adı zaten kayıtlı");

        var supplier = new Supplier
        {
            Name = request.Name,
            ContactEmail = request.ContactEmail,
            Phone = request.Phone,
            Address = request.Address,
            IsActive = true
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);

        return new SupplierDto(
            supplier.Id, supplier.Name, supplier.ContactEmail, supplier.Phone, supplier.Address,
            supplier.IsActive, 0, supplier.CreatedAt, supplier.UpdatedAt);
    }

    public async Task<SupplierDto> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Tedarikçi bulunamadı");

        if (await _db.Suppliers.AnyAsync(s => s.Id != id && s.Name == request.Name, ct))
            throw new ConflictException("Bu tedarikçi adı zaten kayıtlı");

        supplier.Name = request.Name;
        supplier.ContactEmail = request.ContactEmail;
        supplier.Phone = request.Phone;
        supplier.Address = request.Address;
        supplier.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);

        var productCount = await _db.Products.CountAsync(p => p.SupplierId == id, ct);
        return new SupplierDto(
            supplier.Id, supplier.Name, supplier.ContactEmail, supplier.Phone, supplier.Address,
            supplier.IsActive, productCount, supplier.CreatedAt, supplier.UpdatedAt);
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
