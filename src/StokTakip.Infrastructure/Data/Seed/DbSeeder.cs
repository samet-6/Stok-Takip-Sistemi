using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StokTakip.Domain.Entities;
using StokTakip.Domain.Enums;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Data.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var context = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();

        await SeedRolesAsync(roleManager);
        var admin = await SeedAdminAsync(userManager, config);
        await SeedUserAsync(userManager, config);
        await SeedCategoriesAsync(context);
        await SeedSuppliersAsync(context);
        await SeedProductsAndMovementsAsync(context, admin.Id);
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

        var password = config["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is not configured.");

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Admin seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, "Admin");
        return admin;
    }

    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager, IConfiguration config)
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

        var password = config["Seed:UserPassword"]
            ?? throw new InvalidOperationException("Seed:UserPassword is not configured.");

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "User seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "User");
    }

    private static async Task SeedCategoriesAsync(AppDbContext context)
    {
        if (await context.Categories.AnyAsync())
            return;

        context.Categories.AddRange(
            new Category { Name = "Elektronik", Description = "Bilgisayar ve elektronik ürünler" },
            new Category { Name = "Gıda", Description = "Gıda ve içecek ürünleri" },
            new Category { Name = "Kırtasiye", Description = "Ofis ve kırtasiye malzemeleri" },
            new Category { Name = "Temizlik", Description = "Temizlik ve hijyen ürünleri" });

        await context.SaveChangesAsync();
    }

    private static async Task SeedSuppliersAsync(AppDbContext context)
    {
        if (await context.Suppliers.AnyAsync())
            return;

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

    private static async Task SeedProductsAndMovementsAsync(AppDbContext context, string adminId)
    {
        if (await context.Products.AnyAsync())
            return;

        var cats = await context.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var sups = await context.Suppliers.ToDictionaryAsync(s => s.Name, s => s.Id);

        const string el = "Elektronik", gd = "Gıda", kt = "Kırtasiye", tm = "Temizlik";
        const string anadolu = "Anadolu Elektronik A.Ş.", marmara = "Marmara Gıda Ltd. Şti.", ege = "Ege Kırtasiye";

        (StockMovementType, int, string?) Init(int q) => (StockMovementType.In, q, "Başlangıç stoğu");
        (StockMovementType, int, string?) AddIn(int q) => (StockMovementType.In, q, "Ek alım");
        (StockMovementType, int, string?) Sale(int q) => (StockMovementType.Out, q, "Satış");

        var products = new List<Product>
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

        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        Product Build(string name, string sku, string catName, string supName,
            decimal price, int min, bool active, (StockMovementType type, int qty, string? note)[] moves)
        {
            var product = new Product
            {
                Name = name,
                SKU = sku.ToUpperInvariant(),
                CategoryId = cats[catName],
                SupplierId = sups[supName],
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
