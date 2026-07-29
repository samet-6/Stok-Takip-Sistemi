using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Data;
using StokTakip.Infrastructure.Data.Seed;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// The seeder runs on every startup, so "runs twice without changing anything" is not a nicety —
/// it is the only reason restarting the application is safe.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeedTests
{
    private const string CalisanEmail = "user@stok.local";

    private readonly TestDatabaseFixture _db;

    public SeedTests(TestDatabaseFixture db) => _db = db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_ikinci_kez_kostugunda_satir_sayilari_degismiyor()
    {
        var before = await CountsAsync();

        await RunSeederAsync();

        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Seed_sonrasi_stok_miktarlari_hareketlerin_netiyle_ortusuyor()
    {
        await using var db = _db.CreateContext();

        var mismatches = await db.Products
            .Select(p => new
            {
                p.SKU,
                p.StockQuantity,
                Net = p.Movements.Sum(m => m.Type == StockMovementType.In ? m.Quantity : -m.Quantity)
            })
            .Where(x => x.StockQuantity != x.Net)
            .ToListAsync(Ct);

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// The counts are pinned by hand on purpose: a "are there any rows" check would go green on
    /// a seed that silently lost half its products. The demo data is also what the screenshots
    /// and the presentation rest on.
    /// </summary>
    [Fact]
    public async Task Seed_verisinin_sekli_sabit()
    {
        await using var db = _db.CreateContext();

        Assert.Equal(4, await db.Categories.CountAsync(Ct));
        Assert.Equal(3, await db.Suppliers.CountAsync(Ct));
        Assert.Equal(12, await db.Products.CountAsync(Ct));
        Assert.Equal(22, await db.StockMovements.CountAsync(Ct));
        Assert.Equal(0, await db.Notifications.CountAsync(Ct));

        // One passive row on each side: the fixtures every "is it filtered out" test leans on.
        Assert.False(await db.Suppliers.Where(s => s.Name == "Ege Kırtasiye").Select(s => s.IsActive).SingleAsync(Ct));
        Assert.False(await db.Products.Where(p => p.SKU == "DISH-001").Select(p => p.IsActive).SingleAsync(Ct));

        Assert.Equal(80, await StockOfAsync(db, "PAPR-001"));
        Assert.Equal(20, await StockOfAsync(db, "DISH-001"));
        Assert.Equal(120, await StockOfAsync(db, "TRSH-001"));
    }

    /// <summary>
    /// The seeder only fills in what is missing. If it ever "corrected" an existing user, every
    /// restart would quietly undo an admin's edits — and reset a password nobody asked it to.
    /// </summary>
    [Fact]
    public async Task Seed_mevcut_kullaniciya_dokunmuyor()
    {
        const string probe = "T1 seed testi";

        var original = await SetFullNameAsync(probe, Ct);

        try
        {
            await RunSeederAsync();

            await using var db = _db.CreateContext();
            var after = await db.Users.Where(u => u.Email == CalisanEmail).Select(u => u.FullName).SingleAsync(Ct);

            Assert.Equal(probe, after);
        }
        finally
        {
            // Later tests log in as this user; the row goes back exactly as it was.
            await SetFullNameAsync(original, CancellationToken.None);
        }
    }

    private async Task RunSeederAsync()
    {
        using var scope = _db.Factory.Services.CreateScope();

        await DbSeeder.SeedAsync(scope.ServiceProvider);
    }

    private async Task<(int Categories, int Suppliers, int Products, int Movements, int Users)> CountsAsync()
    {
        await using var db = _db.CreateContext();

        return (
            await db.Categories.CountAsync(Ct),
            await db.Suppliers.CountAsync(Ct),
            await db.Products.CountAsync(Ct),
            await db.StockMovements.CountAsync(Ct),
            await db.Users.CountAsync(Ct));
    }

    private static Task<int> StockOfAsync(AppDbContext db, string sku) =>
        db.Products.Where(p => p.SKU == sku).Select(p => p.StockQuantity).SingleAsync(Ct);

    private async Task<string> SetFullNameAsync(string fullName, CancellationToken ct)
    {
        await using var db = _db.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Email == CalisanEmail, ct);
        var previous = user.FullName;
        user.FullName = fullName;
        await db.SaveChangesAsync(ct);

        return previous;
    }
}
