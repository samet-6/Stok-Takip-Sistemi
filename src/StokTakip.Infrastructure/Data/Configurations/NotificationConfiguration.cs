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

        // The list and the bell both read newest-first; the index matches that order so the
        // page and the unread count never have to sort the table.
        builder.HasIndex(n => n.CreatedAt)
            .IsDescending()
            .HasDatabaseName("IX_Notifications_CreatedAt");

        // Serves the rejection de-duplication lookup ("is there an unread rejection for this
        // product already?"), which runs on every refused Out movement.
        builder.HasIndex(n => n.ProductId).HasDatabaseName("IX_Notifications_ProductId");

        builder.ToTable(t =>
            t.HasCheckConstraint("CK_Notifications_Type", "\"Type\" IN (1, 2, 3)"));
    }
}
