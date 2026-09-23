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

        // Every movement list reads "newest first, Id breaking ties" (D17/D20), so each access path
        // gets an index in exactly that order and the page comes off it without a sort step. The
        // two filtered lists — one product's history, one employee's own movements — lead with
        // their filter column; that leading column also serves as the foreign key's index, which
        // is why the single-column ProductId/CreatedByUserId indexes are gone.
        builder.HasIndex(m => new { m.CreatedAt, m.Id })
            .IsDescending(true, true)
            .HasDatabaseName("IX_StockMovements_CreatedAt_Id");
        builder.HasIndex(m => new { m.ProductId, m.CreatedAt, m.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_StockMovements_ProductId_CreatedAt_Id");
        builder.HasIndex(m => new { m.CreatedByUserId, m.CreatedAt, m.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_StockMovements_CreatedByUserId_CreatedAt_Id");

        // D27: one key, one movement — the service checks first, but two copies arriving together
        // both pass that check, and only this index keeps the second one out. Partial: rows from
        // before the key was required have none, and several NULLs are not a duplicate.
        builder.HasIndex(m => m.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("UQ_StockMovements_IdempotencyKey");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_StockMovements_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_StockMovements_Type", "\"Type\" IN (1, 2)");
        });
    }
}
