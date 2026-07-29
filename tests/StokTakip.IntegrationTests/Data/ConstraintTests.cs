using Microsoft.EntityFrameworkCore;
using Npgsql;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Data;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// The API validates before it writes, but validation is code and code gets bypassed. These
/// tests go straight at the database to prove the last line of defence is really there.
/// Every write here is expected to fail, so nothing is committed and no cleanup is needed.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConstraintTests
{
    private readonly TestDatabaseFixture _db;

    public ConstraintTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ayni_SKU_ile_ikinci_kayit_unique_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var (categoryId, supplierId) = await SeedIdsAsync(db);
        var existingSku = await db.Products.Select(p => p.SKU).FirstAsync(Ct);

        db.Products.Add(NewProduct(categoryId, supplierId, existingSku));

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("UQ_Products_SKU", error.ConstraintName);
    }

    [Fact]
    public async Task Sifir_miktarli_stok_hareketi_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);

        db.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            Type = StockMovementType.In,
            Quantity = 0,
            CreatedByUserId = userId
        });

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_StockMovements_Quantity", error.ConstraintName);
    }

    [Fact]
    public async Task Negatif_birim_fiyat_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var (categoryId, supplierId) = await SeedIdsAsync(db);

        var product = NewProduct(categoryId, supplierId, "T1-NEG-PRICE");
        product.UnitPrice = -1m;
        db.Products.Add(product);

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        // Naming the constraint is what separates this from the unique test: a rejection alone
        // would not say which rule did the rejecting.
        Assert.Equal("CK_Products_UnitPrice", error.ConstraintName);
    }

    [Fact]
    public async Task Negatif_stok_miktari_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var (categoryId, supplierId) = await SeedIdsAsync(db);

        var product = NewProduct(categoryId, supplierId, "T1-NEG-STOCK");
        product.StockQuantity = -1;
        db.Products.Add(product);

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Products_StockQuantity", error.ConstraintName);
    }

    /// <summary>
    /// Deliberately raw SQL: EF's own Restrict behaviour throws client-side before a statement
    /// is ever sent, which would prove nothing about the schema. Deleting behind EF's back is
    /// the only way to make the foreign key itself answer.
    /// </summary>
    [Fact]
    public async Task Urunu_olan_kategori_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        var categoryId = await db.Products.Select(p => p.CategoryId).FirstAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "Categories" WHERE "Id" = {0}""", [categoryId], Ct));

        // 23001 (restrict_violation), not the more familiar 23503: EF's DeleteBehavior.Restrict
        // emits ON DELETE RESTRICT, and PostgreSQL reports that refusal under its own code.
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_Products_Categories_CategoryId", error.ConstraintName);
    }

    private async Task<(int CategoryId, int SupplierId)> SeedIdsAsync(AppDbContext db) =>
        (await db.Categories.Select(c => c.Id).FirstAsync(Ct),
         await db.Suppliers.Select(s => s.Id).FirstAsync(Ct));

    private static Product NewProduct(int categoryId, int supplierId, string sku) => new()
    {
        Name = "T1 kisit testi",
        SKU = sku,
        CategoryId = categoryId,
        SupplierId = supplierId,
        UnitPrice = 10m,
        MinStockLevel = 1,
        StockQuantity = 0,
        IsActive = true
    };

    private static async Task<PostgresException> AssertPostgresFailureAsync(AppDbContext db)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        return Assert.IsType<PostgresException>(error.InnerException);
    }
}
