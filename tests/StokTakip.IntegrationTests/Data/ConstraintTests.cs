using Microsoft.EntityFrameworkCore;
using Npgsql;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Data;
using StokTakip.Infrastructure.Identity;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// The API validates before it writes, but validation is code and code gets bypassed. These
/// tests go straight at the database to prove the last line of defence is really there.
/// Every write under test is expected to fail, so nothing is committed and no cleanup is needed;
/// where a test first has to build rows to fail against, it does so in a transaction it never
/// commits.
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

    // D9c at the database itself, service bypassed: "GIDA" is the seeded "Gıda" once both go
    // through f_name_key().
    [Fact]
    public async Task Harf_buyuklugu_farkli_ayni_kategori_unique_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();

        db.Categories.Add(new Category { Name = "GIDA", IsActive = true });

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("UQ_Categories_NameKey", error.ConstraintName);
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

    // D8 at the database itself: lower case, and the "ı" that ToUpperInvariant leaves alone.
    [Theory]
    [InlineData("D8-abc")]
    [InlineData("D8-ABı")]
    public async Task Kural_disi_SKU_check_ihlali_veriyor(string sku)
    {
        await using var db = _db.CreateContext();
        var (categoryId, supplierId) = await SeedIdsAsync(db);

        db.Products.Add(NewProduct(categoryId, supplierId, sku));

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Products_SKU", error.ConstraintName);
    }

    [Fact]
    public async Task Negatif_minimum_stok_seviyesi_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var (categoryId, supplierId) = await SeedIdsAsync(db);

        var product = NewProduct(categoryId, supplierId, "D22-NEG-MIN");
        product.MinStockLevel = -1;
        db.Products.Add(product);

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Products_MinStockLevel", error.ConstraintName);
    }

    [Fact]
    public async Task Tanimsiz_hareket_turu_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);

        db.StockMovements.Add(new StockMovement
        {
            ProductId = productId,
            Type = (StockMovementType)3,
            Quantity = 1,
            CreatedByUserId = userId
        });

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_StockMovements_Type", error.ConstraintName);
    }

    [Fact]
    public async Task Tanimsiz_bildirim_turu_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);

        db.Notifications.Add(new Notification
        {
            Type = (NotificationType)4,
            ProductId = productId,
            Quantity = 0,
            CreatedByUserId = userId
        });

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Notifications_Type", error.ConstraintName);
    }

    // D7: the requested amount belongs to a refused Out movement and only there — without it a
    // rejection cannot say what was asked for, and on any other type it is a number meaning nothing.
    [Theory]
    [InlineData(NotificationType.RejectedOutMovement, null)]
    [InlineData(NotificationType.LowStock, 5)]
    public async Task Istenen_miktar_yalniz_reddedilen_cikista_ve_orada_zorunlu(
        NotificationType type, int? requestedQuantity)
    {
        await using var db = _db.CreateContext();
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);

        var notification = NewNotification(productId, userId);
        notification.Type = type;
        notification.RequestedQuantity = requestedQuantity;
        db.Notifications.Add(notification);

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Notifications_RequestedQuantity", error.ConstraintName);
    }

    [Fact]
    public async Task Negatif_stoklu_bildirim_check_ihlali_veriyor()
    {
        await using var db = _db.CreateContext();
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);

        var notification = NewNotification(productId, userId);
        notification.Quantity = -1;
        db.Notifications.Add(notification);

        var error = await AssertPostgresFailureAsync(db);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_Notifications_Quantity", error.ConstraintName);
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

    [Fact]
    public async Task Urunu_olan_tedarikci_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        var supplierId = await db.Products.Select(p => p.SupplierId).FirstAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "Suppliers" WHERE "Id" = {0}""", [supplierId], Ct));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_Products_Suppliers_SupplierId", error.ConstraintName);
    }

    // Products and users are referenced from two tables each, and PostgreSQL names only the first
    // reference it trips over — a seeded row with both kinds of dependents would let one foreign
    // key answer for the other. So each of the four below builds a fresh principal with exactly
    // one dependent, inside a transaction that is rolled back on dispose.

    [Fact]
    public async Task Hareketi_olan_urun_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var product = await AddFreshProductAsync(db, "D22-FK-MOVE");
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);
        db.StockMovements.Add(NewMovement(product.Id, userId));
        await db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "Products" WHERE "Id" = {0}""", [product.Id], Ct));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_StockMovements_Products_ProductId", error.ConstraintName);
    }

    [Fact]
    public async Task Bildirimi_olan_urun_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var product = await AddFreshProductAsync(db, "D22-FK-NOTE");
        var userId = await db.Users.Select(u => u.Id).FirstAsync(Ct);
        db.Notifications.Add(NewNotification(product.Id, userId));
        await db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "Products" WHERE "Id" = {0}""", [product.Id], Ct));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_Notifications_Products_ProductId", error.ConstraintName);
    }

    [Fact]
    public async Task Hareketi_olan_kullanici_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var userId = await AddFreshUserAsync(db, "d22-fk-move");
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        db.StockMovements.Add(NewMovement(productId, userId));
        await db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "AspNetUsers" WHERE "Id" = {0}""", [userId], Ct));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_StockMovements_AspNetUsers_CreatedByUserId", error.ConstraintName);
    }

    [Fact]
    public async Task Bildirimi_olan_kullanici_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var userId = await AddFreshUserAsync(db, "d22-fk-note");
        var productId = await db.Products.Select(p => p.Id).FirstAsync(Ct);
        db.Notifications.Add(NewNotification(productId, userId));
        await db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "AspNetUsers" WHERE "Id" = {0}""", [userId], Ct));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal("FK_Notifications_AspNetUsers_CreatedByUserId", error.ConstraintName);
    }

    /// <summary>
    /// Append-only was a rule with nowhere to live: the API simply has no update or delete
    /// endpoint for movements, which stops the application and nothing else. These two go at the
    /// table directly — raw SQL, EF bypassed — so whatever reaches the database later (another
    /// service, a migration, someone in pgAdmin) meets the same refusal.
    /// </summary>
    [Fact]
    public async Task Stok_hareketi_veritabani_seviyesinde_guncellenemiyor()
    {
        await using var db = _db.CreateContext();
        // An anonymous type, not a tuple: EF turns a tuple projection into a PostgreSQL record,
        // which Npgsql refuses to read back.
        var movement = await db.StockMovements.Select(m => new { m.Id, m.Quantity }).FirstAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """UPDATE "StockMovements" SET "Quantity" = "Quantity" + 1 WHERE "Id" = {0}""",
                [movement.Id], Ct));

        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Contains("append-only", error.MessageText);

        // A refusal that still wrote would be the worst of both worlds, so the row is read back.
        Assert.Equal(movement.Quantity, await db.StockMovements.Where(m => m.Id == movement.Id)
            .Select(m => m.Quantity).SingleAsync(Ct));
    }

    [Fact]
    public async Task Stok_hareketi_veritabani_seviyesinde_silinemiyor()
    {
        await using var db = _db.CreateContext();
        var id = await db.StockMovements.Select(m => m.Id).FirstAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "StockMovements" WHERE "Id" = {0}""", [id], Ct));

        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.Contains("append-only", error.MessageText);
        Assert.True(await db.StockMovements.AnyAsync(m => m.Id == id, Ct));
    }

    private static async Task<(int CategoryId, int SupplierId)> SeedIdsAsync(AppDbContext db) =>
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

    private static async Task<Product> AddFreshProductAsync(AppDbContext db, string sku)
    {
        var (categoryId, supplierId) = await SeedIdsAsync(db);
        var product = NewProduct(categoryId, supplierId, sku);
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private static async Task<string> AddFreshUserAsync(AppDbContext db, string name)
    {
        var user = new ApplicationUser
        {
            UserName = $"{name}@test.local",
            NormalizedUserName = $"{name}@test.local".ToUpperInvariant(),
            Email = $"{name}@test.local",
            NormalizedEmail = $"{name}@test.local".ToUpperInvariant(),
            FullName = name,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private static StockMovement NewMovement(int productId, string userId) => new()
    {
        ProductId = productId,
        Type = StockMovementType.In,
        Quantity = 1,
        CreatedByUserId = userId
    };

    private static Notification NewNotification(int productId, string userId) => new()
    {
        Type = NotificationType.LowStock,
        ProductId = productId,
        Quantity = 0,
        CreatedByUserId = userId
    };

    private static async Task<PostgresException> AssertPostgresFailureAsync(AppDbContext db)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        return Assert.IsType<PostgresException>(error.InnerException);
    }
}
