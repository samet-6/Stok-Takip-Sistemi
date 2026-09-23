using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Domain.Entities;
using StokTakip.Infrastructure.Identity;

namespace StokTakip.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Id).UseIdentityAlwaysColumn();
        builder.Property(n => n.Type).HasConversion<int>();
        builder.Property(n => n.CreatedByUserId).IsRequired();

        builder.HasOne(n => n.Product)
            .WithMany()
            .HasForeignKey(n => n.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The list reads newest first with Id breaking ties, and the index holds both in that
        // order, so a page comes straight off it with no sort step. (The old CreatedAt-only
        // index left an Incremental Sort for the tiebreak — measured, D21.) The unread count is
        // not this index's job: see the partial one below.
        builder.HasIndex(n => new { n.CreatedAt, n.Id })
            .IsDescending(true, true)
            .HasDatabaseName("IX_Notifications_CreatedAt_Id");

        // The foreign key's index, over every row: deleting a product has to find its read
        // notifications too, which the partial index below does not hold.
        builder.HasIndex(n => n.ProductId).HasDatabaseName("IX_Notifications_ProductId");

        // Unread rows only (D16) — the few the bell counts and the rejection de-duplication asks
        // about ("an unread rejection for this product already?", on every refused Out movement).
        // Measured on 60k rows: the count went from a full table scan to this index.
        builder.HasIndex(n => n.ProductId, "IX_Notifications_Unread_ProductId")
            .HasFilter("\"ReadAt\" IS NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Notifications_Type", "\"Type\" IN (1, 2, 3)");

            // D7: the requested amount belongs to a refused Out movement (3) and only there —
            // required on it, meaningless on any other type.
            t.HasCheckConstraint(
                "CK_Notifications_RequestedQuantity", "(\"Type\" = 3) = (\"RequestedQuantity\" IS NOT NULL)");

            // Stock at the moment of the event; stock itself can never be negative.
            t.HasCheckConstraint("CK_Notifications_Quantity", "\"Quantity\" >= 0");
        });
    }
}
