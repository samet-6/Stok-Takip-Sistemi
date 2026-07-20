using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Domain.Entities;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Data.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.Id).UseIdentityAlwaysColumn();
        builder.Property(m => m.Type).HasConversion<int>();
        builder.Property(m => m.Note).HasMaxLength(300);
        builder.Property(m => m.CreatedByUserId).IsRequired();

        builder.HasOne(m => m.Product)
            .WithMany(p => p.Movements)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.ProductId).HasDatabaseName("IX_StockMovements_ProductId");
        builder.HasIndex(m => m.CreatedAt).HasDatabaseName("IX_StockMovements_CreatedAt");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_StockMovements_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_StockMovements_Type", "\"Type\" IN (1, 2)");
        });
    }
}
