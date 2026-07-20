using Microsoft.EntityFrameworkCore;
using StokTakip.Application.Categories;
using StokTakip.Application.Common;
using StokTakip.Application.Common.Exceptions;
using StokTakip.Domain.Entities;

namespace StokTakip.Application.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IAppDbContext _db;

    public CategoryService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct)
        => await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Description, c.Products.Count, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);

    public async Task<CategoryDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var dto = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Description, c.Products.Count, c.CreatedAt, c.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("Kategori bulunamadı");
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        if (await _db.Categories.AnyAsync(c => c.Name == request.Name, ct))
            throw new ConflictException("Bu kategori adı zaten kayıtlı");

        var category = new Category
        {
            Name = request.Name,
            Description = request.Description
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        return new CategoryDto(
            category.Id, category.Name, category.Description, 0,
            category.CreatedAt, category.UpdatedAt);
    }

    public async Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Kategori bulunamadı");

        if (await _db.Categories.AnyAsync(c => c.Id != id && c.Name == request.Name, ct))
            throw new ConflictException("Bu kategori adı zaten kayıtlı");

        category.Name = request.Name;
        category.Description = request.Description;
        await _db.SaveChangesAsync(ct);

        var productCount = await _db.Products.CountAsync(p => p.CategoryId == id, ct);
        return new CategoryDto(
            category.Id, category.Name, category.Description, productCount,
            category.CreatedAt, category.UpdatedAt);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Kategori bulunamadı");

        var productCount = await _db.Products.CountAsync(p => p.CategoryId == id, ct);
        if (productCount > 0)
            throw new ConflictException(
                $"Bu kategoride {productCount} ürün var, önce ürünleri taşıyın/silin.");

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(ct);
    }
}
