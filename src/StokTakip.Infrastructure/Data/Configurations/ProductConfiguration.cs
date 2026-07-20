using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Domain.Entities;

namespace StokTakip.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.SKU).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.UnitPrice).HasPrecision(18, 2);
        builder.Property(p => p.StockQuantity).HasDefaultValue(0);
        builder.Property(p => p.MinStockLevel).HasDefaultValue(5);
        builder.Property(p => p.IsActive).HasDefaultValue(true);

        builder.HasIndex(p => p.SKU).IsUnique().HasDatabaseName("UQ_Products_SKU");

        // Optimistic concurrency via PostgreSQL's system column "xmin".
        // Npgsql 10 removed the UseXminAsConcurrencyToken() shortcut; the documented
        // replacement is a uint concurrency token mapped to xmin. This is the explicit
        // longhand of that mechanism (uint + OnAddOrUpdate + concurrency token), which
        // the Npgsql convention maps to the xmin system column — so no real column is
        // created in the migration.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_UnitPrice", "\"UnitPrice\" >= 0");
            t.HasCheckConstraint("CK_Products_StockQuantity", "\"StockQuantity\" >= 0");
            t.HasCheckConstraint("CK_Products_MinStockLevel", "\"MinStockLevel\" >= 0");
        });
    }
}
