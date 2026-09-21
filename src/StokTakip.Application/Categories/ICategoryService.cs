namespace StokTakip.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct);
    Task<CategoryDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct);
    /// <summary>
    /// Returns nothing: the endpoint answers 204 and the client refetches (see
    /// docs/api_sozlesmesi.md). A DTO here would be built and thrown away on every request.
    /// </summary>
    Task UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
