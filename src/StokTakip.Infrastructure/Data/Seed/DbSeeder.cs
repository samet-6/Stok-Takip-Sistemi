using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Data.Seed;

/// <summary>
/// Two jobs that used to be one. <b>Bootstrap</b> (roles + the single admin) is what makes the
/// application usable at all — there is no public registration, so without it nobody can ever log
/// in. <b>Demo data</b> (the sample employee, four categories, three suppliers, twelve products)
/// exists for the screenshots and the presentation, and is an all-or-nothing package.
///
/// Keeping them apart is what stops a half-filled database from bringing the application down:
/// the demo block is guarded once, as a whole, instead of table by table.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, bool includeDemoData)
    {
        var context = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbSeeder));

        await SeedRolesAsync(roleManager);
        var admin = await SeedAdminAsync(userManager, config);

        if (!includeDemoData)
        {
            logger.LogInformation("Demo seed atlandi: 'Seed:Demo' kapali.");
            return;
        }

        await SeedDemoAsync(context, userManager, config, admin.Id, logger);
    }

    /// <summary>
    /// One gate for the whole demo package. The old per-table guards let the product block run
    /// against a catalogue somebody had since renamed, and the lookup by name then threw on
    /// startup — the application did not boot at all. A single "has this catalogue been used?"
    /// check makes that state unreachable: either the demo rows are written together, or none of
    /// them are.
    /// </summary>
    private static async Task SeedDemoAsync(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        string adminId,
        ILogger logger)
    {
        if (await context.Categories.AnyAsync()
            || await context.Suppliers.AnyAsync()
            || await context.Products.AnyAsync())
        {
            logger.LogInformation("Demo seed atlandi: katalogda zaten veri var.");
            return;
        }

        await SeedUserAsync(userManager, config, logger);
        await SeedCategoriesAsync(context);
        await SeedSuppliersAsync(context);
        await SeedProductsAndMovementsAsync(context, adminId, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task<ApplicationUser> SeedAdminAsync(
        UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        const string adminEmail = "admin@stok.local";
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is not null)
            return admin;

        admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Sistem Yöneticisi"
        };

        // Bootstrap, not demo data: this is deliberately a hard startup failure. There is no
        // public registration, so a fresh database without an admin password produces an
        // application nobody can ever log into — coming up "successfully" would hide that.
        // Restarts never reach this line, because an existing admin returns above.
        var password = config["Seed:AdminPassword"]
            ?? throw new InvalidOperationException(
                "Seed:AdminPassword tanimli degil. Bos bir veritabaninda yonetici hesabi bu " +
                "parolayla olusturulur; verilmezse uygulamaya hicbir kullanici giris edemez.");

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Admin seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, "Admin");
        return admin;
    }

    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager, IConfiguration config, ILogger logger)
    {
        const string userEmail = "user@stok.local";
        if (await userManager.FindByEmailAsync(userEmail) is not null)
            return;

        var user = new ApplicationUser
        {
            UserName = userEmail,
            Email = userEmail,
            EmailConfirmed = true,
            FullName = "Örnek Çalışan"
            // IsActive + CreatedAt filled by DB defaults (see ApplicationUserConfiguration).
        };

        // Demo data, unlike the admin above: a missing password skips the sample employee and
        // leaves the application perfectly usable, so it must never keep the host from starting.
        var password = config["Seed:UserPassword"];
        if (password is null)
        {
            logger.LogWarning(
                "Ornek calisan hesabi atlandi: 'Seed:UserPassword' tanimli degil.");
            return;
        }

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "User seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "User");
    }

    // The per-table guards these three methods used to carry are gone: SeedDemoAsync decides once,
    // for the package as a whole, so a guard here could only ever disagree with it.
    private static async Task SeedCategoriesAsync(AppDbContext context)
    {
        context.Categories.AddRange(
            new Category { Name = "Elektronik", Description = "Bilgisayar ve elektronik ürünler" },
            new Category { Name = "Gıda", Description = "Gıda ve içecek ürünleri" },
            new Category { Name = "Kırtasiye", Description = "Ofis ve kırtasiye malzemeleri" },
            new Category { Name = "Temizlik", Description = "Temizlik ve hijyen ürünleri" });

        await context.SaveChangesAsync();
    }

    private static async Task SeedSuppliersAsync(AppDbContext context)
    {
        context.Suppliers.AddRange(
            new Supplier
            {
                Name = "Anadolu Elektronik A.Ş.",
                ContactEmail = "satis@anadoluelektronik.com",
                Phone = "0312 555 1010",
                Address = "Ankara",
                IsActive = true
            },
            new Supplier
            {
                Name = "Marmara Gıda Ltd. Şti.",
                ContactEmail = "info@marmaragida.com",
                Phone = "0216 555 2020",
                Address = "İstanbul",
                IsActive = true
            },
            new Supplier
            {
                Name = "Ege Kırtasiye",
                ContactEmail = "siparis@egekirtasiye.com",
                Phone = "0232 555 3030",
                Address = "İzmir",
                IsActive = false
            });

        await context.SaveChangesAsync();
    }

    private static async Task SeedProductsAndMovementsAsync(
        AppDbContext context, string adminId, ILogger logger)
    {
        var cats = await context.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var sups = await context.Suppliers.ToDictionaryAsync(s => s.Name, s => s.Id);

        const string el = "Elektronik", gd = "Gıda", kt = "Kırtasiye", tm = "Temizlik";
        const string anadolu = "Anadolu Elektronik A.Ş.", marmara = "Marmara Gıda Ltd. Şti.", ege = "Ege Kırtasiye";

        (StockMovementType, int, string?) Init(int q) => (StockMovementType.In, q, "Başlangıç stoğu");
        (StockMovementType, int, string?) AddIn(int q) => (StockMovementType.In, q, "Ek alım");
        (StockMovementType, int, string?) Sale(int q) => (StockMovementType.Out, q, "Satış");

        var products = new List<Product?>
        {
            Build("Kablosuz Mouse", "MOUSE-001", el, anadolu, 349.90m, 5, true, new[] { Init(50), Sale(8) }),
            Build("Mekanik Klavye", "KEYB-001", el, anadolu, 899.00m, 5, true, new[] { Init(30), Sale(5) }),
            Build("USB-C Kablo", "USBC-001", el, anadolu, 79.90m, 10, true, new[] { Init(20), Sale(12) }),
            Build("Taşınabilir SSD 1TB", "SSD1-001", el, anadolu, 1899.00m, 3, true, new[] { Init(5), Sale(3) }),
            Build("Filtre Kahve 1kg", "COFF-001", gd, marmara, 249.00m, 8, true, new[] { Init(60), Sale(10), AddIn(5) }),
            Build("Yeşil Çay 500g", "TEA-001", gd, marmara, 129.90m, 8, true, new[] { Init(40), Sale(6) }),
            Build("Zeytinyağı 1L", "OLIV-001", gd, marmara, 399.00m, 6, true, new[] { Init(10), Sale(5) }),
            Build("A4 Fotokopi Kağıdı", "PAPR-001", kt, ege, 189.00m, 10, true, new[] { Init(100), Sale(20) }),
            Build("Tükenmez Kalem 50'li", "PEN-001", kt, ege, 149.90m, 12, true, new[] { Init(80) }),
            Build("Yüzey Temizleyici 750ml", "CLEN-001", tm, marmara, 59.90m, 10, true, new[] { Init(50) }),
            Build("Çöp Poşeti 30L", "TRSH-001", tm, anadolu, 39.90m, 15, true, new[] { Init(120) }),
            Build("Bulaşık Deterjanı 1.5L", "DISH-001", tm, marmara, 89.90m, 8, false, new[] { Init(30), Sale(10) })
        };

        context.Products.AddRange(products.OfType<Product>());
        await context.SaveChangesAsync();

        // Nullable on purpose. SeedDemoAsync only lets this run against an empty catalogue, so the
        // two lookups below cannot miss — the rows were written moments ago. The check stays
        // anyway because the seeder runs during startup: whatever goes wrong here must end up in
        // the log, never as an exception that keeps the application from booting.
        Product? Build(string name, string sku, string catName, string supName,
            decimal price, int min, bool active, (StockMovementType type, int qty, string? note)[] moves)
        {
            if (!cats.TryGetValue(catName, out var categoryId) ||
                !sups.TryGetValue(supName, out var supplierId))
            {
                logger.LogWarning(
                    "Demo urunu '{Sku}' atlandi: '{Category}' kategorisi veya '{Supplier}' tedarikcisi bulunamadi.",
                    sku, catName, supName);

                return null;
            }

            var product = new Product
            {
                Name = name,
                SKU = sku.ToUpperInvariant(),
                CategoryId = categoryId,
                SupplierId = supplierId,
                UnitPrice = price,
                MinStockLevel = min,
                IsActive = active,
                StockQuantity = 0
            };

            var net = 0;
            foreach (var (type, qty, note) in moves)
            {
                product.Movements.Add(new StockMovement
                {
                    Type = type,
                    Quantity = qty,
                    Note = note,
                    CreatedByUserId = adminId
                });
                net += type == StockMovementType.In ? qty : -qty;
            }

            product.StockQuantity = net;
            return product;
        }
    }
}
