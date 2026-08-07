using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StokTakip.Application.Common;
using StokTakip.Domain.Entities;

namespace StokTakip.Infrastructure.Data.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired()
            .UseCollation(PostgresText.TurkishCollation);
        builder.Property(s => s.ContactEmail).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName("UQ_Suppliers_Name");

        // Product search also matches on the supplier name — same search key as everywhere else.
        builder.Property<string>(SearchText.NameFolded)
            .HasMaxLength(150)
            .HasComputedColumnSql(PostgresText.Folded("Name"), stored: true);

        builder.HasIndex(SearchText.NameFolded)
            .HasDatabaseName("IX_Suppliers_NameFolded")
            .HasMethod("gin")
            .HasOperators(PostgresText.TrigramOperators);
    }
}
