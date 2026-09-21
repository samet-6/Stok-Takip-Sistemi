namespace StokTakip.Application.Suppliers;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> GetAllAsync(CancellationToken ct);
    Task<SupplierDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<SupplierDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct);
    /// <summary>
    /// Returns nothing: the endpoint answers 204 and the client refetches (see
    /// docs/api_sozlesmesi.md). A DTO here would be built and thrown away on every request.
    /// </summary>
    Task UpdateAsync(int id, UpdateSupplierRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
