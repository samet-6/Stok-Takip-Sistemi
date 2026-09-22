using Microsoft.EntityFrameworkCore;
using StokTakip.Domain.Entities;
using StokTakip.Infrastructure.Data;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// Values nobody types in: the audit stamps <c>ApplyAudit</c> writes, and the defaults the
/// schema fills when a column is left out. Every test runs in a transaction it never commits, and
/// reads back through a cleared change tracker so the answer comes from the database, not from
/// the entity EF still holds in memory.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditAndDefaultTests
{
    private readonly TestDatabaseFixture _db;

    public AuditAndDefaultTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Npgsql stores microseconds, DateTime carries 100 ns ticks — a stamp taken just after
    // "before" can come back a hair earlier than it.
    private static readonly TimeSpan StorageRounding = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task Eklenen_satir_istemcinin_verdigi_degil_kendi_damgasini_aliyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var forged = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var category = new Category
        {
            Name = "D23 damga",
            IsActive = true,
            CreatedAt = forged,
            UpdatedAt = forged
        };

        var before = DateTime.UtcNow;
        db.Categories.Add(category);
        await db.SaveChangesAsync(Ct);
        var after = DateTime.UtcNow;

        var stored = await ReadStampsAsync(db, category.Id);
        Assert.InRange(stored.CreatedAt, before - StorageRounding, after);
        // Born in one SaveChanges, so "last changed" is the same instant as "created".
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task Guncelleme_UpdatedAt_i_ilerletiyor_CreatedAt_a_dokunmuyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var category = new Category { Name = "D23 guncelleme", IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync(Ct);
        var created = await ReadStampsAsync(db, category.Id);

        var tracked = await db.Categories.SingleAsync(c => c.Id == category.Id, Ct);
        tracked.Description = "degisti";
        await db.SaveChangesAsync(Ct);

        var updated = await ReadStampsAsync(db, category.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt,
            $"UpdatedAt ilerlemedi: {created.UpdatedAt:O} -> {updated.UpdatedAt:O}");
    }

    /// <summary>
    /// Raw SQL on purpose: through EF these columns are always sent (or deliberately withheld,
    /// see below), so only an INSERT that leaves them out shows what the schema itself supplies.
    /// </summary>
    [Fact]
    public async Task Urun_varsayilanlari_semadaki_degerler()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var categoryId = await db.Categories.Select(c => c.Id).FirstAsync(Ct);
        var supplierId = await db.Suppliers.Select(s => s.Id).FirstAsync(Ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Products" ("Name", "SKU", "CategoryId", "SupplierId", "UnitPrice", "CreatedAt", "UpdatedAt")
            VALUES ('D23 varsayilan', 'D23-DEFAULTS', {0}, {1}, 1, now(), now())
            """, [categoryId, supplierId], Ct);

        var stored = await db.Products.Where(p => p.SKU == "D23-DEFAULTS")
            .Select(p => new { p.StockQuantity, p.MinStockLevel, p.IsActive })
            .SingleAsync(Ct);
        Assert.Equal(0, stored.StockQuantity);
        Assert.Equal(5, stored.MinStockLevel);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Kullanici_varsayilanlari_semadaki_degerler()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var id = Guid.NewGuid().ToString();

        var before = DateTime.UtcNow;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUsers" ("Id", "FullName", "EmailConfirmed", "PhoneNumberConfirmed",
                                       "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({0}, 'D23 varsayilan', false, false, false, true, 0)
            """, [id], Ct);
        var after = DateTime.UtcNow;

        var stored = await db.Users.Where(u => u.Id == id)
            .Select(u => new { u.CreatedAt, u.IsActive })
            .SingleAsync(Ct);
        // now() is frozen at BEGIN, a moment before "before" was read — the window is widened to
        // "during this test", which is all the default has to satisfy.
        Assert.InRange(stored.CreatedAt, before - TimeSpan.FromMinutes(1), after);
        Assert.True(stored.IsActive);
    }

    /// <summary>
    /// A store default and an explicit value can collide in EF: a property holding its CLR
    /// default (0 for an int) is treated as "not set" and left out of the INSERT, so the
    /// database default steps in. For MinStockLevel that would silently turn an admin's
    /// deliberate 0 ("never warn me") into 5.
    /// </summary>
    [Fact]
    public async Task Sifir_minimum_stokla_eklenen_urun_sifir_kaliyor()
    {
        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var product = new Product
        {
            Name = "D23 sifir esik",
            SKU = "D23-MIN-ZERO",
            CategoryId = await db.Categories.Select(c => c.Id).FirstAsync(Ct),
            SupplierId = await db.Suppliers.Select(s => s.Id).FirstAsync(Ct),
            UnitPrice = 1m,
            MinStockLevel = 0,
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        var stored = await db.Products.Where(p => p.Id == product.Id)
            .Select(p => p.MinStockLevel).SingleAsync(Ct);
        Assert.Equal(0, stored);
    }

    private static async Task<(DateTime CreatedAt, DateTime UpdatedAt)> ReadStampsAsync(
        AppDbContext db, int categoryId)
    {
        db.ChangeTracker.Clear();
        // An anonymous type, not a tuple: EF turns a tuple projection into a PostgreSQL record,
        // which Npgsql refuses to read back.
        var row = await db.Categories.Where(c => c.Id == categoryId)
            .Select(c => new { c.CreatedAt, c.UpdatedAt }).SingleAsync(Ct);
        return (row.CreatedAt, row.UpdatedAt);
    }
}
