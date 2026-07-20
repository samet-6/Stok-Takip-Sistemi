using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Domain.Entities;

namespace StokTakip.Infrastructure.Data.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.ContactEmail).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName("UQ_Suppliers_Name");
    }
}
