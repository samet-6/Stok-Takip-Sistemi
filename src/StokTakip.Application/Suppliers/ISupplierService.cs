namespace StokTakip.Application.Suppliers;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct);
    Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct);
    Task<SupplierDto> UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
