using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Data;
using StokTakip.IntegrationTests.Api;
using Xunit;

namespace StokTakip.IntegrationTests.Data;

/// <summary>
/// Startup scenarios, which the shared test database cannot host: proving that a half-emptied
/// catalogue still boots means deleting every seeded product, and every later test class leans on
/// those rows. So each test here creates a throwaway database of its own, boots the real
/// application against it, and drops it afterwards.
///
/// Booting is the point. The failure these guard against was never a bad row — it was the host
/// refusing to start, which no assertion against an already-running host can see.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeedScenarioTests : IAsyncLifetime
{
    // No fixture is injected: these tests own their databases. The collection membership is what
    // matters — it serialises this class against the rest, which is required because booting a
    // host here rewrites process-wide environment variables.
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// One freshly seeded database for the whole class, built once. The tests that read it never
    /// write to it, and nothing else in the run knows it exists — which is what makes their
    /// assertions independent of the order the suite happens to run in (O28).
    /// </summary>
    private ScenarioDatabase _pristine = null!;

    public async ValueTask InitializeAsync()
    {
        _pristine = await ScenarioDatabase.CreateAsync("temiz");

        // Booting is what seeds it; the host is not needed afterwards, the data is.
        await using (_pristine.Boot(demoFlag: "true")) { }
    }

    public async ValueTask DisposeAsync() => await _pristine.DisposeAsync();

    /// <summary>
    /// The counts are pinned by hand on purpose: an "are there any rows" check would go green on
    /// a seed that silently lost half its products. The demo data is also what the screenshots
    /// and the presentation rest on.
    /// </summary>
    [Fact]
    public async Task Taze_seed_verisinin_sekli_sabit()
    {
        await using var db = _pristine.CreateContext();

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

    [Fact]
    public async Task Taze_seedde_stok_miktarlari_hareketlerin_netiyle_ortusuyor()
    {
        await using var db = _pristine.CreateContext();

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
    /// Shape alone let the text drift: the development database ended up holding "Ege Kirtasiye"
    /// and "A4 Fotokopi Kagidi" in plain ASCII while the seeder said otherwise, and every count
    /// stayed green through it.
    ///
    /// Turkish letters are what drift, so those are what the expectations below pin: U+0131 ı ·
    /// U+011F ğ · U+015F ş · U+015E Ş. A folded row and a correct expectation no longer agree,
    /// which is the whole point.
    ///
    /// What this pins is the seed *source*. It cannot see a database that was filled before the
    /// text was fixed — this one is created and seeded from scratch.
    /// </summary>
    [Fact]
    public async Task Taze_seed_adlari_turkce_yazimi_koruyor()
    {
        await using var db = _pristine.CreateContext();

        Assert.Equal(
            Ordinal(["Elektronik", "Gıda", "Kırtasiye", "Temizlik"]),
            Ordinal(await db.Categories.Select(c => c.Name).ToListAsync(Ct)));

        // "Ege Kırtasiye" is the row that was measured as plain ASCII in the development database.
        Assert.Equal(
            Ordinal(["Anadolu Elektronik A.Ş.", "Marmara Gıda Ltd. Şti.", "Ege Kırtasiye"]),
            Ordinal(await db.Suppliers.Select(s => s.Name).ToListAsync(Ct)));

        // The eight seeded products whose names carry a Turkish letter — the only ones that can
        // drift. Keyed by SKU so the assertion does not depend on insertion order. PAPR-001 is
        // the second row that had drifted ("A4 Fotokopi Kagidi").
        var expected = new Dictionary<string, string>
        {
            ["SSD1-001"] = "Taşınabilir SSD 1TB",
            ["TEA-001"] = "Yeşil Çay 500g",
            ["OLIV-001"] = "Zeytinyağı 1L",
            ["PAPR-001"] = "A4 Fotokopi Kağıdı",
            ["PEN-001"] = "Tükenmez Kalem 50'li",
            ["CLEN-001"] = "Yüzey Temizleyici 750ml",
            ["TRSH-001"] = "Çöp Poşeti 30L",
            ["DISH-001"] = "Bulaşık Deterjanı 1.5L"
        };

        var actual = await db.Products
            .Where(p => expected.Keys.Contains(p.SKU))
            .ToDictionaryAsync(p => p.SKU, p => p.Name, Ct);

        Assert.Equal(expected, actual);
    }

    private static Task<int> StockOfAsync(AppDbContext db, string sku) =>
        db.Products.Where(p => p.SKU == sku).Select(p => p.StockQuantity).SingleAsync(Ct);

    /// <summary>
    /// Ordinal, not culture-aware: the point is to compare code points, and a Turkish collation
    /// would sort "Gıda" and "Gida" as neighbours — the very confusion under test.
    /// </summary>
    private static List<string> Ordinal(IEnumerable<string> names) =>
        [.. names.OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>
    /// The original defect (§1.1 #5): the guards were per table, so renaming a category and
    /// clearing the products left the product block running against a catalogue whose keys had
    /// moved. It looked them up by name, threw KeyNotFoundException during startup, and the
    /// application never came up — a rename made through the ordinary admin screen was enough.
    /// </summary>
    [Fact]
    public async Task Kategori_yeniden_adlandirilip_urunler_silinince_uygulama_yine_aciliyor()
    {
        await using var scenario = await ScenarioDatabase.CreateAsync("d6");

        // First boot fills the demo catalogue, exactly as a fresh install would.
        await using (scenario.Boot(demoFlag: "true")) { }

        const string renamed = "Elektronik Urunler";
        await using (var db = scenario.CreateContext())
        {
            var category = await db.Categories.SingleAsync(c => c.Name == "Elektronik", Ct);
            category.Name = renamed;

            await db.Notifications.ExecuteDeleteAsync(Ct);
            await db.StockMovements.ExecuteDeleteAsync(Ct);
            await db.Products.ExecuteDeleteAsync(Ct);
            await db.SaveChangesAsync(Ct);
        }

        // The assertion is that this line returns at all.
        await using var host = scenario.Boot(demoFlag: "true");

        await using var verify = scenario.CreateContext();
        Assert.True(await verify.Categories.AnyAsync(c => c.Name == renamed, Ct));

        // All or nothing: the catalogue has been used, so the demo package stays out of it rather
        // than half-filling a database somebody has since edited.
        Assert.Equal(0, await verify.Products.CountAsync(Ct));
        Assert.Equal(4, await verify.Categories.CountAsync(Ct));
    }

    /// <summary>
    /// D28/B37: a real deployment should not receive a second login and twelve sample products
    /// nobody asked for. The switch is an explicit flag rather than the environment name, because
    /// the compose stack runs as Production and *is* the demo — it turns the flag on itself.
    /// </summary>
    [Fact]
    public async Task Production_ortaminda_bayrak_yoksa_demo_seed_kosmuyor()
    {
        await using var scenario = await ScenarioDatabase.CreateAsync("d28");

        await using var host = scenario.Boot(environment: "Production", demoFlag: null);

        await using var db = scenario.CreateContext();

        // Bootstrap still runs: without the admin nobody could ever log in, and there is no
        // public registration to fall back on.
        Assert.Equal(2, await db.Roles.CountAsync(Ct));
        Assert.Equal("admin@stok.local", await db.Users.Select(u => u.Email).SingleAsync(Ct));

        Assert.Equal(0, await db.Categories.CountAsync(Ct));
        Assert.Equal(0, await db.Suppliers.CountAsync(Ct));
        Assert.Equal(0, await db.Products.CountAsync(Ct));
    }

    /// <summary>
    /// B37: the sample employee's password used to be a startup precondition — miss it and the
    /// host threw before serving a request. Demo data must never be able to do that. The admin
    /// password is deliberately not part of this: an empty database with no admin password
    /// produces an application nobody can log into, and that should fail loudly.
    /// </summary>
    [Fact]
    public async Task Ornek_calisan_parolasi_yokken_uygulama_aciliyor()
    {
        await using var scenario = await ScenarioDatabase.CreateAsync("b37");

        await using var host = scenario.Boot(demoFlag: "true", userPassword: null);

        await using var db = scenario.CreateContext();

        Assert.Equal("admin@stok.local", await db.Users.Select(u => u.Email).SingleAsync(Ct));

        // The rest of the demo package is unaffected — only the account that had no password.
        Assert.Equal(12, await db.Products.CountAsync(Ct));
    }

    /// <summary>
    /// A database of its own, plus the environment plumbing needed to boot a host against it.
    /// Configuration reaches Program.cs through environment variables (see StokTakipFactory for
    /// why), and those are process-wide — so every variable this touches is put back before the
    /// method returns, or the next host built in this run would inherit it.
    /// </summary>
    private sealed class ScenarioDatabase : IAsyncDisposable
    {
        private static readonly string[] Variables =
        [
            "ASPNETCORE_ENVIRONMENT",
            "ConnectionStrings__Default",
            "Seed__Demo",
            "Seed__AdminPassword",
            "Seed__UserPassword"
        ];

        private readonly string _connectionString;

        private ScenarioDatabase(string connectionString) => _connectionString = connectionString;

        public static async Task<ScenarioDatabase> CreateAsync(string suffix)
        {
            var template = Environment.GetEnvironmentVariable("STOKTAKIP_TEST_DB")!;
            var builder = new NpgsqlConnectionStringBuilder(template);

            // Never the suite's own database: this class drops what it points at, and the two
            // names differ by a suffix.
            builder.Database = $"{builder.Database}_{suffix}";

            var scenario = new ScenarioDatabase(builder.ConnectionString);
            await scenario.DropAsync();

            return scenario;
        }

        public AppDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);

        /// <summary>
        /// Boots the application as shipped. Migration and seeding happen inside this call, so
        /// anything that would keep the host from starting surfaces here.
        /// </summary>
        public WebApplicationFactory<Program> Boot(
            string environment = "Testing",
            string? demoFlag = null,
            string? adminPassword = StokTakipFactory.AdminPassword,
            string? userPassword = StokTakipFactory.UserPassword)
        {
            var saved = Variables.ToDictionary(n => n, Environment.GetEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
                Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);
                Environment.SetEnvironmentVariable("Seed__Demo", demoFlag);
                Environment.SetEnvironmentVariable("Seed__AdminPassword", adminPassword);
                Environment.SetEnvironmentVariable("Seed__UserPassword", userPassword);

                var factory = new WebApplicationFactory<Program>();

                // The host is built lazily; touching Services is what actually runs Program.cs.
                _ = factory.Services;

                return factory;
            }
            finally
            {
                foreach (var (name, value) in saved)
                    Environment.SetEnvironmentVariable(name, value);
            }
        }

        public async ValueTask DisposeAsync() => await DropAsync();

        private async Task DropAsync()
        {
            // A disposed host leaves pooled connections behind, and PostgreSQL refuses to drop a
            // database that still has any.
            NpgsqlConnection.ClearAllPools();

            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
