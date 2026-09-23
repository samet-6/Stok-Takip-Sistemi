using System.Linq.Expressions;
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

    /// <summary>
    /// The one definition of what a CategoryDto is. Every read goes through it, so a new field
    /// is added here once instead of in each query — and ProductCount is always counted the same
    /// way (in SQL, over the live rows) rather than passed in by whoever happens to build the DTO.
    /// </summary>
    private static readonly Expression<Func<Category, CategoryDto>> ToDto =
        c => new CategoryDto(
            c.Id, c.Name, c.Description, c.IsActive, c.DeactivatedAt,
            c.Products.Count, c.RowVersion, c.CreatedAt, c.UpdatedAt);

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct)
        => await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(ToDto)
            .ToListAsync(ct);

    public async Task<CategoryDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var dto = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("Kategori bulunamadı");
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var name = CleanName(request.Name);
        if (await NameTakenAsync(name, exceptId: null, ct))
            throw new ConflictException("Bu kategori adı zaten kayıtlı");

        // Born active, like suppliers: switching one off is a separate, deliberate edit.
        var category = new Category
        {
            Name = name,
            Description = request.Description,
            IsActive = true
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        // Read back through the one projection instead of assembling a second DTO by hand
        // (the pattern ProductService already uses). Hand-assembly had to invent ProductCount —
        // a literal 0 that is true only because a category cannot be born with products.
        return (await GetByIdAsync(category.Id, ct))!;
    }

    public async Task UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Kategori bulunamadı");

        var name = CleanName(request.Name);
        if (await NameTakenAsync(name, exceptId: id, ct))
            throw new ConflictException("Bu kategori adı zaten kayıtlı");

        category.Name = name;
        category.Description = request.Description;
        category.IsActive = request.IsActive;

        // Optimistic concurrency, as in ProductService: a stale RowVersion makes the UPDATE match
        // 0 rows, raising DbUpdateConcurrencyException (→ 409 concurrency_conflict).
        _db.Entry(category).Property(c => c.RowVersion).OriginalValue = request.RowVersion;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cleaned, and refused if nothing visible is left: a zero-width space passes [Required]
    /// (.NET does not count it as whitespace) but would store a name nobody can see.
    /// </summary>
    private static string CleanName(string raw)
    {
        var name = NameText.Clean(raw);
        return name.Length > 0 ? name : throw new BadRequestException("Kategori adı boş olamaz");
    }

    /// <summary>
    /// Asks the database, through the same f_name_key() that generates the unique column — the
    /// pre-check and the index answer "is this name taken" with one rule.
    /// </summary>
    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken ct) =>
        _db.Categories.AnyAsync(
            c => c.Id != exceptId && EF.Property<string>(c, NameText.NameKey) == NameText.Key(name), ct);

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
